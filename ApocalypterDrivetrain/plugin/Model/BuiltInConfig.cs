// The example drivetrain that ships with the mod (used when [Drivetrain] ConfigPath is empty).
// README.md §4 shows this exact text; verify/ fails if the two drift apart.
namespace ApocalypterDrivetrain.Model
{
    public static class BuiltInConfig
    {
        public const string Json = @"{
  ""schema"": 1,
  ""name"": ""Example 4x4 pickup"",
  ""engine"": {
    ""idleRpm"": 800,
    ""redlineRpm"": 5600,
    ""revLimitRpm"": 6000,
    ""revLimiterCutTime"": 0.12,
    ""inertia"": 0.25,
    ""torqueCurve"": {
      ""points"": [[800, 210], [2000, 290], [3500, 320], [5000, 280], [6000, 220]]
    }
  },
  ""clutch"": {
    ""capacity"": 650,
    ""engagementRpm"": 1100,
    ""engagementRange"": 400,
    ""engagementCurve"": [[0, 0], [0.4, 0.15], [1, 1]],
    ""launch"": { ""rpm"": 2500 }
  },
  ""gearbox"": {
    ""forward"": [4.2, 2.5, 1.6, 1.15, 0.85],
    ""reverse"": [-3.8],
    ""finalDrive"": 3.9,
    ""shiftLogic"": ""automatic"",
    ""upshiftRpm"": 4800,
    ""downshiftRpm"": 2000,
    ""shiftTime"": 0.25,
    ""output"": ""centre""
  },
  ""differentials"": [
    { ""name"": ""centre"", ""type"": ""torqueSplit"", ""split"": 0.4, ""outputs"": [""front"", ""rear""] },
    { ""name"": ""front"", ""type"": ""open"", ""outputs"": [""front.L"", ""front.R""] },
    { ""name"": ""rear"", ""type"": ""limitedSlip"", ""stiffness"": 0.6, ""slipTorque"": 900, ""powerRamp"": 0.8, ""coastRamp"": 0.4, ""outputs"": [""rear.L"", ""rear.R""] }
  ],
  ""axles"": [
    { ""name"": ""front"", ""z"": 1.45, ""track"": 1.62, ""steered"": true },
    { ""name"": ""rear"", ""z"": -1.55, ""track"": 1.64 }
  ]
}
";
    }
}
