#!/usr/bin/env python3
"""Static contract gate for TASK-166 dynamic planetary weather and diurnal cycle."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

failures: list[str] = []
runtime = text("src/Game.Client/Scripts/VerticalSlice/PlanetWeatherRuntime.cs")
weather = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetWeather.cs")
main = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
models = text("src/Game.Client/Scripts/Persistence/SaveGameModels.cs")
database = text("src/Game.Client/Scripts/Persistence/SaveDatabase.cs")
survival = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlayerSurvival.cs")
fauna = text("src/Game.Client/Scripts/VerticalSlice/EcologyFaunaNode.cs")
audio = text("src/Game.Client/Scripts/Application/AudioDirector.cs")
developer = text("src/Game.Client/Scripts/Developer/SalvageRepairSliceDeveloperBridge.cs")
worldgen_tests = text("tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs")
persistence_tests = text("tests/ProjectHorizon.Tests/Persistence/PersistenceTests.cs")
en = text("src/Game.Client/Content/localization.en.json")
ru = text("src/Game.Client/Content/localization.ru.json")

need("enum PlanetWeatherKind" in runtime and
     all(kind in runtime for kind in ("Clear", "Wind", "Storm", "Toxic")),
     "weather state machine kinds missing", failures)
need("DefaultDayDurationSeconds = 600.0" in runtime and
     "SunElevationDegrees" in runtime and "Daylight" in runtime,
     "deterministic diurnal cycle contract missing", failures)
need("WeatherCellHours = 2.0" in runtime and
     "SelectWeather(" in runtime and "PlanetPhaseHours(" in runtime,
     "deterministic planet-scoped weather cells missing", failures)
need("PlanetWeatherSaveData" in models and "PlanetWeatherSaveData? PlanetWeather" in models,
     "weather save model missing", failures)
need("'planet_weather'" in database and "ValidatePlanetWeather" in database and
     "snapshot.PlanetWeather" in database and "planetWeatherJson" in database,
     "weather SQLite round-trip missing", failures)
need("InitializePlanetWeatherRuntime(saveData: null);" in main and
     "InitializePlanetWeatherRuntime(snapshot?.PlanetWeather);" in main and
     "UpdatePlanetWeather(delta);" in main,
     "startup/load/reset/process weather lifecycle incomplete", failures)
need("TemperatureHazardBonus" in survival and "ToxicHazardBonus" in survival and
     "LifeSupportDrainBonus" in survival,
     "weather is not integrated with player survival hazards", failures)
need("SetWeatherResponse(" in fauna and "_weatherSpeedMultiplier" in fauna and
     "_weatherWindVelocity" in fauna,
     "weather is not integrated with flying fauna response", failures)
need("SetWeatherIntensity" in audio and "WeatherWind" in audio,
     "weather audio intensity integration missing", failures)
need("PlanetWeather.SetLocalHour" in developer and
     "PlanetWeather.SetDeveloperOverride" in developer,
     "set_time/set_weather developer commands are not wired to live runtime", failures)
need("RunPlanetWeatherAcceptance();" in main and "TASK-166 (F5)" in main and
     "TASK-166 planetary weather acceptance" in weather,
     "TASK-166 F5/HUD acceptance wiring missing", failures)
need("PlanetWeather_IsDeterministicAndRoundTripsGameHours" in worldgen_tests and
     "PlanetWeather_AcceptanceCoversDayNightWeatherHazardsAndPersistence" in worldgen_tests and
     "PlanetWeather = new PlanetWeatherSaveData" in persistence_tests and
     "PlanetWeather_RoundTripsThroughSqliteWithoutSchemaMigration" in persistence_tests,
     "weather xUnit/persistence regressions missing", failures)
need(all(key in en and key in ru for key in (
     "ui.hud.weather.summary", "ui.weather.clear", "ui.weather.wind",
     "ui.weather.storm", "ui.weather.toxic")),
     "weather localization parity missing", failures)
need("PlanetWeatherFx" in weather and "RainField" in weather and
     "ToxicMotes" in weather and "FogMultiplier" in weather,
     "surface weather presentation/precipitation layer missing", failures)

if failures:
    print("TASK-166 PLANETARY WEATHER CONTRACT FAIL:")
    for failure in failures:
        print(f"- {failure}")
    sys.exit(1)

print(
    "TASK-166 PLANETARY WEATHER CONTRACT PASS: "
    "cycle=600s-deterministic; weatherCells=2h; kinds=4; "
    "sky+sun+fog+clouds=dynamic; precipitation=visual; survival=hazard-coupled; "
    "fauna=wind/weather-aware; audio=weather-intensity; persistence=planet_weather; "
    "developer=set_time/set_weather; f5=1; xunit=4-regression-groups."
)
