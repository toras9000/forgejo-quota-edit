#r "nuget: ForgejoApiClient, 11.0.0-rev.1"
#r "nuget: Lestaly, 0.76.0"
#r "nuget: Kokuban, 0.2.0"
#load "../.env-helper.csx"
#nullable enable
using System.Threading;
using ForgejoApiClient;
using ForgejoApiClient.Api;
using Kokuban;
using Lestaly;
using Lestaly.Cx;

var settings = new
{
    // サービスのURL
    ServiceURL = new Uri("http://localhost:9950"),

    // トークン保存ファイル
    ApiKeyFile = ThisSource.RelativeFile("../.auth-forgejo-api"),

    // 作成するQuota Ruleの定義
    QuotaRules = new CreateQuotaRuleOptions[]
    {
        new(name: "default-rule", limit: 500 * 1024 * 1024, subjects: ["size:all"]),
        new(name: "packages-narrow", limit: 100 * 1024 * 1024, subjects: ["size:assets:packages:all"]),
    },

    // 作成するQuota Groupの定義
    QuotaGroups = new CreateQuotaGroupOptions[]
    {
        new(name: "default-group", rules:[new(name: "default-rule")]),
        new(name: "packages-limited", rules:[new(name: "packages-narrow")]),
    },

    // 作成するQuota Groupの定義
    UserAssigns = new[]
    {
        new { Group = "packages-limited", User = "test-user", },
    },
};

var noInteract = Args.Any(a => a == "--no-interact");
var pauseMode = noInteract ? PavedPause.None : PavedPause.Any;

return await Paved.RunAsync(config: c => c.PauseOn(pauseMode), action: async () =>
{
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);
    using var signal = new SignalCancellationPeriod();

    WriteLine("APIトークンを読み込み ...");
    var forgejoToken = await settings.ApiKeyFile.ScriptScrambler().LoadTokenAsync() ?? throw new Exception("トークン情報を読み取れない");
    if (forgejoToken.Service.AbsoluteUri != settings.ServiceURL.AbsoluteUri) throw new Exception("保存情報が対象と合わない");

    WriteLine("クライアント準備 ...");
    using var forgejo = new ForgejoClient(forgejoToken.Service, forgejoToken.Token);
    var me = default(User);
    using (var breaker = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
    {
        // 初期化直後はAPI呼び出しがエラーとなることがあるようなので、一定時間繰り返し呼び出しを試みる。
        while (me == null || me.login == null)
        {
            try { me = await forgejo.User.GetMeAsync(signal.Token); }
            catch { await Task.Delay(500); }
        }
    }

    WriteLine("クォータルールの作成 ...");
    var rules = (await forgejo.Admin.ListQuotaRulesAsync(signal.Token)).ToList();
    foreach (var options in settings.QuotaRules)
    {
        WriteLine($"  QuotaRule: {Chalk.Blue[options.name!]}");
        Write("  .. ");
        if (rules.Any(g => g.name == options.name))
        {
            WriteLine(Chalk.Gray["既に存在する"]);
            continue;
        }

        try
        {
            var rule = await forgejo.Admin.CreateQuotaRuleAsync(options, signal.Token);
            rules.Add(rule);
            WriteLine(Chalk.Green["作成"]);
        }
        catch (Exception ex)
        {
            WriteLine(Chalk.Red[ex.Message]);
        }
    }

    WriteLine("クォータグループの作成 ...");
    var groups = (await forgejo.Admin.ListQuotaGroupsAsync(signal.Token)).ToList();
    foreach (var options in settings.QuotaGroups)
    {
        WriteLine($"  QuotaGroup: {Chalk.Blue[options.name!]}");
        Write("  .. ");
        if (groups.Any(g => g.name == options.name))
        {
            WriteLine(Chalk.Gray["既に存在する"]);
            continue;
        }

        try
        {
            var group = await forgejo.Admin.CreateQuotaGroupAsync(options, signal.Token);
            groups.Add(group);
            WriteLine(Chalk.Green["作成"]);
        }
        catch (Exception ex)
        {
            WriteLine(Chalk.Red[ex.Message]);
        }
    }

    WriteLine("クォータグループの割当 ...");
    foreach (var assign in settings.UserAssigns)
    {
        WriteLine($"  Assign: {Chalk.Blue[assign.Group]} to {Chalk.Blue[assign.User]}");
        Write("  .. ");
        try
        {
            await forgejo.Admin.AddQuotaGroupUserAsync(assign.Group, assign.User, signal.Token);
            WriteLine(Chalk.Green["割当"]);
        }
        catch (Exception ex)
        {
            WriteLine(Chalk.Red[ex.Message]);
        }
    }

});
