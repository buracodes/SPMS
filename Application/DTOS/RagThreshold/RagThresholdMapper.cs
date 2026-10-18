using Application.Dashboard;

namespace Application.DTOS.RagThreshold
{
    public static class RagThresholdMapper
    {
        public static RagThresholdsDto ToDto(this RagThresholdsConfiguration configuration)
        {
            return new RagThresholdsDto
            {
                ScopeKey = configuration.ScopeKey,
                Green = configuration.Green,
                Yellow = configuration.Yellow,
                IsConfigured = configuration.IsConfigured,
                Source = configuration.Source,
                GreenColor = configuration.GreenColor,
                YellowColor = configuration.YellowColor,
                RedColor = configuration.RedColor,
                GrayColor = configuration.GrayColor,
                Warnings = configuration.Warnings
            };
        }
    }
}
