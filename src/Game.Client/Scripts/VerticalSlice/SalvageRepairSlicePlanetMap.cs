using System;
using System.Linq;
using System.Text;
using Godot;

public partial class SalvageRepairSlice
{
    private PanelContainer? _planetMapPanel;
    private Label? _planetMapLabel;
    private bool _planetMapOpen;

    private void BindPlanetMapSceneNodes()
    {
        _planetMapPanel = GetNodeOrNull<PanelContainer>("Hud/PlanetMap");
        _planetMapLabel = GetNodeOrNull<Label>("Hud/PlanetMap/Label");
        if (_planetMapPanel is null || _planetMapLabel is null)
        {
            throw new InvalidOperationException(
                "Vertical slice scene is missing TASK-130 planet map panel.");
        }
        _planetMapPanel.Visible = false;
    }

    private bool HandlePlanetMapAction()
    {
        if (_planetMapOpen)
        {
            ClosePlanetMap(L("ui.planet_map.closed"));
            return true;
        }

        if ((_state != SalvageRepairSliceState.Ready &&
             _state != SalvageRepairSliceState.Passed) ||
            _stationServicesOpen || _baseBuildMode || _discoveryCatalogOpen ||
            _shipManagementOpen || _galaxyMapOpen || _ecologyCatalogOpen ||
            _missionJournalOpen || _playerEquipmentOpen || _npcInteractionOpen)
        {
            return false;
        }

        OpenPlanetMap();
        return true;
    }

    private bool HandlePlanetMapInput(Key physical, Key logical)
    {
        if (!_planetMapOpen)
        {
            return false;
        }
        if (Matches(physical, logical, Key.Escape))
        {
            ClosePlanetMap(L("ui.planet_map.closed"));
        }
        return true;
    }

    private void OpenPlanetMap()
    {
        if (_planetMapPanel is null || _planetMapLabel is null)
        {
            return;
        }
        CloseRecipeSelector();
        CloseStationServices();
        CloseBaseBuildMode();
        CloseDiscoveryCatalog();
        CloseShipManagement();
        CloseGalaxyMap();
        CloseEcologyCatalog();
        CloseMissionJournal();
        ClosePlayerEquipment();
        CloseNpcInteraction();
        _planetMapOpen = true;
        _planetMapPanel.Visible = true;
        UpdatePlanetMapPanel();
        _status = L("ui.planet_map.opened");
    }

    private void ClosePlanetMap(string status = "")
    {
        _planetMapOpen = false;
        if (_planetMapPanel is not null)
        {
            _planetMapPanel.Visible = false;
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            _status = status;
        }
    }

    private void UpdatePlanetMapPanel()
    {
        if (!_planetMapOpen || _planetMapLabel is null ||
            _planetaryExplorationRuntime is null || _player is null)
        {
            return;
        }

        PlanetSurfaceLogicalPosition logicalPlayer =
            GetPlanetSurfaceLogicalPlayerPosition();
        PlanetEnvironmentProfile environment = PlanetEnvironment.BuildProfile(
            GalaxyNavigation.CurrentPlanet,
            GalaxyNavigation.CurrentSystem.StarType);
        PlanetSurfaceTopologyRuntime topology = new(environment.RadiusKm);
        PlanetSurfaceGeographicAddress geographic = topology.FromLogical(
            logicalPlayer.EastMeters,
            logicalPlayer.NorthMeters);
        const int width = 33;
        const int height = 19;
        const double mapHalfExtentMeters = 40.0;
        double minX = logicalPlayer.EastMeters - mapHalfExtentMeters;
        double maxX = logicalPlayer.EastMeters + mapHalfExtentMeters;
        double minZ = logicalPlayer.NorthMeters - mapHalfExtentMeters;
        double maxZ = logicalPlayer.NorthMeters + mapHalfExtentMeters;
        char[,] cells = new char[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                cells[y, x] = (x % 4 == 0 && y % 3 == 0) ? '+' : '·';
            }
        }

        foreach (PlanetaryPoiRuntimeState state in PlanetaryExploration.States)
        {
            int x = MapCoordinate(state.Placement.PositionX, minX, maxX, width);
            int y = MapCoordinate(state.Placement.PositionZ, minZ, maxZ, height);
            char marker = state.Resolved ? 'X' : state.Discovered ? 'O' : '?';
            cells[height - 1 - y, x] = cells[height - 1 - y, x] is '@' or 'O' or 'X' or '?'
                ? '*'
                : marker;
        }

        int playerX = MapCoordinate(logicalPlayer.EastMeters, minX, maxX, width);
        int playerY = MapCoordinate(logicalPlayer.NorthMeters, minZ, maxZ, height);
        cells[height - 1 - playerY, playerX] = '@';

        StringBuilder map = new();
        for (int y = 0; y < height; y++)
        {
            map.Append('│');
            for (int x = 0; x < width; x++)
            {
                map.Append(cells[y, x]);
            }
            map.AppendLine("│");
        }

        string nearest = PlanetaryExploration.States
            .Where(state => state.Discovered)
            .OrderBy(state => topology.GreatCircleDistanceMeters(
                logicalPlayer.EastMeters,
                logicalPlayer.NorthMeters,
                state.Placement.PositionX,
                state.Placement.PositionZ))
            .Select(state =>
                $"{PlanetaryExploration.DisplayName(state)} " +
                $"({state.Placement.PositionX:0.0}, {state.Placement.PositionZ:0.0}) " +
                $"{topology.GreatCircleDistanceMeters(logicalPlayer.EastMeters, logicalPlayer.NorthMeters, state.Placement.PositionX, state.Placement.PositionZ):0}m")
            .FirstOrDefault() ?? "none discovered";

        _planetMapLabel.Text =
            "PLANET MAP • LOCAL SURFACE REGION\n" +
            $"Planet: {GalaxyNavigation.CurrentPlanetId} • " +
            $"Player X/Z: {logicalPlayer.EastMeters:0.0}/{logicalPlayer.NorthMeters:0.0}\n" +
            $"Lat/Lon: {geographic.LatitudeDegrees:0.0000}°/{geographic.LongitudeDegrees:0.0000}° • " +
            $"R={environment.RadiusKm:0.0}km • C={topology.CircumferenceMeters / 1000.0:0.0}km\n" +
            "Legend: @ player • ? unknown POI • O discovered • X resolved • * overlap\n" +
            map +
            $"Discovery: {PlanetaryExploration.DiscoveredCount}/{PlanetaryExploration.States.Count} • " +
            $"Resolved: {PlanetaryExploration.ResolvedCount} • nearest discovered: {nearest}\n" +
            "N / Esc close • P scanner • J discovery catalogue";
    }

    private static int MapCoordinate(double value, double min, double max, int cells)
    {
        double t = Math.Clamp((value - min) / (max - min), 0.0, 1.0);
        return Math.Clamp((int)Math.Round(t * (cells - 1)), 0, cells - 1);
    }
}
