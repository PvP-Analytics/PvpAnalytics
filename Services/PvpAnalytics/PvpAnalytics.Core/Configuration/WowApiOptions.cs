using System.ComponentModel.DataAnnotations;
using PvpAnalytics.Shared.Constants;

namespace PvpAnalytics.Core.Configuration;

/// <summary>
/// Configuration options for Blizzard WoW API integration.
/// </summary>
public class WowApiOptions
{
    public const string SectionName = "WowApi";

    [Required(ErrorMessage = AppConstants.ErrorMessages.WowApiClientIdRequired)]
    public string ClientId { get; set; } = string.Empty;

    [Required(ErrorMessage = AppConstants.ErrorMessages.WowApiClientSecretRequired)]
    public string ClientSecret { get; set; } = string.Empty;
    public string EuOAuthUrl { get; set; } = "https://eu.battle.net/oauth/token";
    public string UsOAuthUrl { get; set; } = "https://us.battle.net/oauth/token";
    public string EuApiBaseUrl { get; set; } = "https://eu.api.blizzard.com";
    public string UsApiBaseUrl { get; set; } = "https://us.api.blizzard.com";
}