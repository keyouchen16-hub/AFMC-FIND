namespace Afmc.Find.App;
public static class ProductInfo
{
    public static string VersionLabel => "v" + typeof(ProductInfo).Assembly.GetName().Version!.ToString(3);
    public static string Title => "AFMC FIND " + VersionLabel;
    public const string FeedbackEmail = "keyouchen16@gmail.com";
    public static string FeedbackUri => "mailto:" + FeedbackEmail + "?subject=" + Uri.EscapeDataString(Title + " 使用反馈");
}
