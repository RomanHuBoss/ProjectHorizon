using Godot;

public partial class SalvageRepairSlice
{
    private string _galaxyExpeditionAcceptanceHud = "READY";
    private bool? _galaxyExpeditionAcceptancePassed;
    private bool _galaxyExpeditionReadyPrinted;

    private void PrintGalaxyExpeditionReady()
    {
        if (_galaxyExpeditionReadyPrinted)
        {
            return;
        }
        _galaxyExpeditionReadyPrinted = true;
        GD.Print(
            "TASK-210 100-system procedural expedition READY: " +
            "criterion=spec-41>=100-distinct-systems; generation=on-demand; " +
            "path=neighbor-sector-corridor; jumpRange=550ly-validation; " +
            "manualPerSystemContent=0-except-starter; detailedSystemResident=1; " +
            "visitedState=stable-ids-only; wholeGalaxyResident=0; F5=acceptance.");
    }

    private void RunGalaxyExpeditionAcceptance()
    {
        GalaxyExpeditionReport report = GalaxyExpeditionAcceptanceRunner.Run(
            ShipSystemsCatalog);
        _galaxyExpeditionAcceptancePassed = report.Passed;
        _galaxyExpeditionAcceptanceHud = report.Passed
            ? $"PASS systems={report.SystemsVisited} resident={report.MaximumResidentSystemDefinitions} maxJump={report.MaximumJumpDistanceLightYears:0}ly"
            : $"FAIL systems={report.SystemsVisited} jumps={report.JumpsApplied}";
        if (report.Passed)
        {
            GD.Print(report.BuildOutputLine());
        }
        else
        {
            GD.PushError(report.BuildOutputLine());
        }
    }
}
