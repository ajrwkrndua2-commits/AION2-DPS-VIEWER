namespace Aion2Dashboard.Models;

public sealed class AdBannerConfig
{
    public bool Enabled { get; set; } = true;
    public string Badge { get; set; } = "AD";
    public string Title { get; set; } = "DPSVIEWER 후원 배너";
    public string Description { get; set; } = "배너 문구와 링크는 ad-banner.json에서 수정할 수 있습니다.";
    public string ButtonText { get; set; } = "열기";
    public string Url { get; set; } = string.Empty;
}
