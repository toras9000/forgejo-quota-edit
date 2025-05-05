#r "nuget: Lestaly, 0.79.0"
#load "../.env-helper.csx"
#nullable enable
using Lestaly;
using Lestaly.Cx;

var settings = new
{
    // サービスのURL
    ServiceURL = new Uri("http://localhost:9950"),

    // トークン生成対象ユーザ名
    TargetUser = "forgejo-admin",

    // トークン名
    TokenName = "quota-test-token",

    // トークン保存ファイル
    ApiKeyFile = ThisSource.RelativeFile("../.auth-forgejo-api"),
};

return await Paved.ProceedAsync(noPause: Args.RoughContains("--no-interact"), async () =>
{
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);

    WriteLine("テスト用 APIトークンの生成 ...");
    var composeFile = ThisSource.RelativeFile("./docker/compose.yml");
    var apiToken = await "docker".args("compose", "--file", composeFile,
        "exec", "-u", "1000", "app",
        "forgejo", "admin", "user", "generate-access-token",
            "--username", settings.TargetUser,
            "--token-name", settings.TokenName,
            "--scopes", "all",
            "--raw"
    ).silent().result().success().output();

    WriteLine("トークンをファイルに保存 ...");
    await settings.ApiKeyFile.ScriptScrambler().SaveTokenAsync(settings.ServiceURL, apiToken);

    WriteLine("完了");
});
