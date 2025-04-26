#r "nuget: ForgejoApiClient, 11.0.0-rev.1"
#r "nuget: Kokuban, 0.2.0"
#r "nuget: Lestaly, 0.75.0"
#load ".env-helper.csx"
#nullable enable
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using ForgejoApiClient;
using ForgejoApiClient.Api;
using Kokuban;
using Lestaly;
using Lestaly.Cx;

// 設定
var settings = new
{
    // Forgejo 関連の情報
    Forgejo = new
    {
        // サービスURL
        ServiceURL = new Uri("http://localhost:9950/"),

        // APIキー保存ファイル
        ApiTokenFile = ThisSource.RelativeFile(".auth-forgejo-api"),
    },
};

// コマンド定義
var CommandDefinitions = new CommandDefine("root", "", Subs:
[
    new("rule", "クォータルール関連", Aliases: ["r", "rules"], Subs:
    [
        new("list",   "  ルールリスト取得", Handler: cmdRuleListAsync, Aliases: ["l"]),
        new("info",   "  ルール情報表示", Handler: cmdRuleInfoAsync, Aliases: ["i"]),
        new("create", "  ルール作成", Handler: cmdRuleCreateAsync),
        new("delete", "  ルール削除", Handler: cmdRuleDeleteAsync),
    ]),
    new("group", "クォータグループ関連", Aliases: ["g", "groups"], Subs:
    [
        new("list",   "  グループリスト取得", Handler: cmdGroupListAsync, Aliases: ["l"]),
        new("info",   "  グループ情報表示", Handler: cmdGroupInfoAsync, Aliases: ["i"]),
        new("create", "  グループ作成", Handler: cmdGroupCreateAsync),
        new("delete", "  グループ削除", Handler: cmdGroupDeleteAsync),
        new("rule",   "  グループのルール関連", Aliases: ["r", "rules"], Subs:
        [
            new("add",    "    ルール追加", Handler: cmdGroupRuleAddAsync),
            new("remove", "    ルール除去", Handler: cmdGroupRuleRemoveAsync)
        ]),
        new("user", "  グループのユーザ関連", Aliases: ["u", "users"], Subs:
        [
            new("list",   "    ユーザリスト取得", Handler: cmdGroupUserListAsync, Aliases: ["l"]),
            new("add",    "    ユーザ追加", Handler: cmdGroupUserAddAsync),
            new("remove", "    ユーザ除去", Handler: cmdGroupUserRemoveAsync)
        ]),
    ]),
    new("user", "ユーザのクォータ情報関連", Aliases: ["u", "users"], Subs:
    [
        new("info",   "  クォータ情報取得", Handler: cmdUserInfoAsync, Aliases: ["i"]),
    ]),
    new("help", "コマンド一覧", Handler: cmdHelpAsync, Aliases: ["h", "?"]),
]);

// メイン処理
return await Paved.RunAsync(async () =>
{
    // コンソール準備
    using var signal = new SignalCancellationPeriod();
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);

    // タイトル出力
    void WriteScriptTitle()
    {
        const string ScriptTitle = "ForgejoのQuota管理ヘルパ";
        WriteLine(ScriptTitle);
        WriteLine($"  Forgejo   : {settings.Forgejo.ServiceURL}");
        WriteLine();
    }

    // 認証情報を準備
    WriteScriptTitle();
    WriteLine("サービス認証情報の準備");
    var forgejoToken = await settings.Forgejo.ApiTokenFile.BindTokenAsync("Forgejo APIトークン", settings.Forgejo.ServiceURL, signal.Token);
    Clear();
    WriteScriptTitle();

    // APIクライアントを準備
    WriteLine("Forgejo クライアントの生成 ...");
    using var forgejo = new ForgejoClient(forgejoToken.Service, forgejoToken.Token);
    var apiUser = await forgejo.User.GetMeAsync(cancelToken: signal.Token);
    WriteLine(Chalk.Gray[$"  .. User: {apiUser.login}"]);
    WriteLine();
    if (apiUser.is_admin != true) throw new PavedMessageException("APIトークンのユーザが管理者ではない。", PavedMessageKind.Warning);

    // 情報をまとめた実行コンテキスト情報を生成
    var context = new ManageContext(forgejo, signal.Token);

    // コマンド処理
    WriteLine("コマンド入力 ('help' でコマンドリスト表示)");
    while (true)
    {
        // コマンド入力
        Write(">");
        var input = ReadLine();
        if (input == null) break;
        if (input.IsWhite()) continue;

        try
        {
            // コマンド定義取得
            var define = CommandDefinitions.SelectCommand(input.AsMemory(), out var args);
            if (define?.Handler == null) throw new PavedMessageException("コマンドが正しくない", PavedMessageKind.Warning);

            // コマンドハンドラを実行
            await define.Handler.Invoke(context, args);
        }
        catch (Exception ex)
        {
            var color = (ex as PavedMessageException)?.Kind switch
            {
                PavedMessageKind.Information => Chalk.Gray,
                PavedMessageKind.Warning or PavedMessageKind.Cancelled => Chalk.Yellow,
                _ => Chalk.Red,
            };
            WriteLine(color[ex.Message]);
        }
        WriteLine();
    }
});

/// <summary>実行コンテキスト情報</summary>
/// <param name="Client">APIクライアント</param>
/// <param name="Breaker">実行中断トークン</param>
record ManageContext(ForgejoClient Client, CancellationToken Breaker);

/// <summary>コマンドハンドラデリゲート型</summary>
/// <param name="context">実行コンテキスト情報</param>
/// <param name="arguments">引数文字列</param>
delegate ValueTask AsyncCommmandHandler(ManageContext context, ReadOnlyMemory<char> arguments);

/// <summary>コマンド定義</summary>
/// <param name="Token">コマンド名</param>
/// <param name="Description">コマンド概要</param>
/// <param name="Subs">サブコマンド</param>
/// <param name="Handler">コマンド処理ハンドラ</param>
/// <param name="Aliases">コマンド名のエイリアス</param>
record CommandDefine(string Token, string Description, CommandDefine[]? Subs = default, AsyncCommmandHandler? Handler = default, string[]? Aliases = default);

/// <summary>コマンド関連の定数</summary>
static class CommandConstants
{
    /// <summary>コマンドトークン区切り</summary>
    public static char[] Separators = [' ', '\t'];
}

/// <summary>コマンドラインから実行するコマンドを選択する</summary>
/// <param name="root">入力コマンドライン</param>
/// <param name="input">入力コマンドライン</param>
/// <param name="args">コマンドラインの引数部分</param>
/// <returns>実行するコマンド定義</returns>
static CommandDefine? SelectCommand(this CommandDefine root, ReadOnlyMemory<char> input, out ReadOnlyMemory<char> args)
{
    // コマンド定義のサブコマンドから一致する物を選択する
    static CommandDefine? matchSubCommand(CommandDefine site, ReadOnlySpan<char> token)
    {
        // サブコマンドを持たない場合は一致無し
        if (site.Subs == null) return null;

        // サブコマンドの検索
        foreach (var sub in site.Subs)
        {
            // サブコマンド一致判定
            if (token.Equals(sub.Token, StringComparison.OrdinalIgnoreCase)) return sub;
            // エイリアスがある場合はそれに一致する物を検索
            if (sub.Aliases == null) continue;
            foreach (var alias in sub.Aliases)
            {
                if (token.Equals(alias, StringComparison.OrdinalIgnoreCase)) return sub;
            }
        }
        return null;
    }

    // 最も深く一致するコマンド定義を検索
    var stage = root;
    var scan = input.Span;
    while (true)
    {
        // コマンドラインの最初のトークンに一致する定義の取得を試みる
        var token = scan.TakeSkipTokenAny(out var next, CommandConstants.Separators);
        var match = matchSubCommand(stage, token);
        if (match == null) break;

        // 一致したら次へ
        stage = match;
        scan = next;
    }

    // 有効なコマンドエントリでなければ一致コマンド無しの返却値
    if (stage.Handler == null)
    {
        args = ReadOnlyMemory<char>.Empty;
        return null;
    }

    // コマンドラインの残りをコマンド引数として返す
    args = input[^scan.Length..];

    return stage;
}

/// <summary>文字列に指定のトークンが含まれるかを判定する</summary>
/// <param name="self">文字列</param>
/// <param name="target">判定するトークン</param>
/// <returns>含まれるか否か</returns>
static bool HasToken(this ReadOnlySpan<char> self, ReadOnlySpan<char> target)
{
    target = target.Trim();
    var scan = self;
    while (!scan.IsEmpty)
    {
        var token = scan.TakeSkipTokenAny(out scan, CommandConstants.Separators);
        if (token.Equals(target, StringComparison.OrdinalIgnoreCase)) return true;
    }
    return false;
}

/// <summary>文字列に指定のトークンが含まれるかを判定する</summary>
/// <param name="self">文字列</param>
/// <param name="target">判定するトークン</param>
/// <returns>含まれるか否か</returns>
static bool HasToken(this ReadOnlyMemory<char> self, ReadOnlySpan<char> target)
    => self.Span.HasToken(target);

/// <summary>文字列から最初のトークンを取得する</summary>
/// <param name="self">文字列</param>
/// <param name="target">判定するトークン</param>
/// <returns>トークン</returns>
static ReadOnlySpan<char> TakeArgument(this ReadOnlyMemory<char> self, out ReadOnlyMemory<char> next)
{
    var token = self.Span.TakeSkipTokenAny(out var after, CommandConstants.Separators);
    next = self[^after.Length..];
    return token;
}

/// <summary>文字列からトークンリストを取得する</summary>
/// <param name="self">文字列</param>
/// <returns>トークンリスト</returns>
static List<string> TakeArgList(this ReadOnlyMemory<char> self)
{
    var list = new List<string>();
    var scan = self.Span;
    while (!scan.IsEmpty)
    {
        var token = scan.TakeSkipTokenAny(out scan, CommandConstants.Separators);
        list.Add(token.ToString());
    }
    return list;
}

/// <summary>文字列を単位付きサイズ値としてパースする</summary>
/// <param name="self">文字列</param>
/// <returns>サイズ値</returns>
static long ParseHumanizedSize(this ReadOnlySpan<char> self)
{
    var text = self.Trim();
    if (text.IsEmpty) return default;

    // 単位があれば係数に変換
    var unitCoeff = text[^1] switch
    {
        'K' => 1024L,
        'M' => 1024L * 1024L,
        'G' => 1024L * 1024L * 1024L,
        'T' => 1024L * 1024L * 1024L * 1024L,
        'P' => 1024L * 1024L * 1024L * 1024L * 1024L,
        'E' => 1024L * 1024L * 1024L * 1024L * 1024L * 1024L,
        _ => default(long?),
    };

    // 係数があればパース値と係数を反映した値を返却
    if (unitCoeff.HasValue)
    {
        var number = text[..^1].ParseNumber<long>();
        number = checked(number * unitCoeff.Value);
        return number;
    }

    // 係数がなければそのままパース
    return text.ParseNumber<long>();
}

/// <summary>未実装コマンド</summary>
async ValueTask cmdNotImplementedAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    await Task.CompletedTask;
    WriteLine(Chalk.Yellow["コマンド未実装"]);
}

/// <summary>help コマンド</summary>
async ValueTask cmdHelpAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    await Task.CompletedTask;

    // コマンド一覧を列挙
    static IEnumerable<(string token, string desc)> enumCommands(int level, CommandDefine[] commands)
    {
        var indent = string.Create(level * 2, ' ', (span, state) => span.Fill(state));
        foreach (var cmd in commands)
        {
            yield return ($"{indent}{cmd.Token}", cmd.Description);
            if (cmd.Subs == null) continue;
            foreach (var subCmd in enumCommands(level + 1, cmd.Subs))
            {
                yield return subCmd;
            }
        }
    }

    // コマンド一覧を出力
    var cmdList = enumCommands(level: 0, CommandDefinitions.Subs!).ToArray();
    var tokenWidth = cmdList.Max(c => c.token.Length);
    foreach (var cmd in cmdList)
    {
        WriteLine($"{cmd.token.PadRight(tokenWidth)} : {cmd.desc}");
    }
}

/// <summary>rule list コマンド</summary>
async ValueTask cmdRuleListAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    // ヘルプ表示
    if (arguments.HasToken("--help"))
    {
        WriteLine("rule list [--detail]");
        WriteLine("    --detail : 含まれる制限対象を表示する");
        return;
    }

    // パラメータ取得
    var detail = arguments.HasToken("--detail");

    // 情報取得・表示
    var rules = await context.Client.Admin.ListQuotaRulesAsync(context.Breaker);
    foreach (var rule in rules)
    {
        WriteLine($"Name={rule.name}, Limit={(rule.limit ?? 0).ToHumanize()}, {rule.subjects?.Count ?? 0} subjects");
        if (detail && rule.subjects != null)
        {
            foreach (var subject in rule.subjects)
            {
                WriteLine($"  {subject}");
            }
        }
    }
}

/// <summary>rule info コマンド</summary>
async ValueTask cmdRuleInfoAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    // ヘルプ表示
    if (arguments.HasToken("--help"))
    {
        WriteLine("rule info <name>");
        WriteLine("    name : 対象ルール名称");
        return;
    }

    // パラメータ取得
    var name = arguments.TakeArgument(out arguments).ToString();

    // 検証
    if (name.IsWhite()) throw new PavedMessageException($"{nameof(name)} は必須です。");

    // 情報取得・表示
    var rule = await context.Client.Admin.GetQuotaRuleAsync(name, context.Breaker);
    WriteLine($"Name: {rule.name}");
    WriteLine($"Limit: {(rule.limit ?? 0).ToHumanize()}");
    if (0 < rule.subjects?.Count)
    {
        WriteLine($"Subjects:");
        foreach (var subject in rule.subjects)
        {
            WriteLine($" - {subject}");
        }
    }
}

/// <summary>rule create コマンド</summary>
async ValueTask cmdRuleCreateAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    // ヘルプ表示
    if (arguments.HasToken("--help"))
    {
        WriteLine("rule create <name> <limit> <subject> [<subject>..]");
        WriteLine("    name    : 対象ルール名称");
        WriteLine("    limit   : 制限サイズ");
        WriteLine("    subject : 制限対象");
        return;
    }

    // パラメータ取得
    var name = arguments.TakeArgument(out arguments).ToString();
    var limit = arguments.TakeArgument(out arguments).ParseHumanizedSize();
    var subjects = arguments.TakeArgList();

    // 検証
    if (name.IsWhite()) throw new PavedMessageException($"{nameof(name)} は必須です。");
    if (0 < subjects.Count) throw new PavedMessageException($"1つ以上の subject を指定する必要があります。");

    // 実行・結果表示
    var options = new CreateQuotaRuleOptions(name: name, limit: limit, subjects: subjects);
    var rule = await context.Client.Admin.CreateQuotaRuleAsync(options, context.Breaker);
    WriteLine($"Name: {rule.name}");
    WriteLine($"Limit: {(rule.limit ?? 0).ToHumanize()}");
    if (0 < rule.subjects?.Count)
    {
        WriteLine($"Subjects:");
        foreach (var subject in rule.subjects)
        {
            WriteLine($" - {subject}");
        }
    }
}

/// <summary>rule delete コマンド</summary>
async ValueTask cmdRuleDeleteAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    // ヘルプ表示
    if (arguments.HasToken("--help"))
    {
        WriteLine("rule delete <name>");
        WriteLine("    name    : 対象ルール名称");
        return;
    }

    // パラメータ取得
    var name = arguments.TakeArgument(out arguments).ToString();

    // 検証
    if (name.IsWhite()) throw new PavedMessageException($"{nameof(name)} は必須です。");

    // 実行・結果表示
    await context.Client.Admin.DeleteQuotaRuleAsync(name, context.Breaker);
    WriteLine($"ルール '{name}' を削除しました。");
}

/// <summary>group list コマンド</summary>
async ValueTask cmdGroupListAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    // ヘルプ表示
    if (arguments.HasToken("--help"))
    {
        WriteLine("group list [--detail]");
        WriteLine("    --detail : 含まれるルールを表示する");
        return;
    }

    // パラメータ取得
    var detail = arguments.HasToken("--detail");

    // 情報取得・表示
    var groups = await context.Client.Admin.ListQuotaGroupsAsync(context.Breaker);
    foreach (var group in groups)
    {
        WriteLine($"Name={group.name}, {group.rules?.Count ?? 0} rules");
        if (detail && group.rules != null)
        {
            foreach (var rule in group.rules)
            {
                WriteLine($"  Name={rule.name}, Limit={(rule.limit ?? 0).ToHumanize()}, {rule.subjects?.Count ?? 0} subjects");
            }
        }
    }
}

/// <summary>group list コマンド</summary>
async ValueTask cmdGroupInfoAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    // ヘルプ表示
    if (arguments.HasToken("--help"))
    {
        WriteLine("group info <name>");
        WriteLine("    name : 対象グループ名称");
        return;
    }

    // パラメータ取得
    var name = arguments.TakeArgument(out arguments).ToString();

    // 検証
    if (name.IsWhite()) throw new PavedMessageException($"{nameof(name)} は必須です。");

    // 情報取得・表示
    var group = await context.Client.Admin.GetQuotaGroupAsync(name, context.Breaker);
    WriteLine($"Name={group.name}");
    if (0 < group.rules?.Count)
    {
        WriteLine($"Rules:");
        foreach (var rule in group.rules)
        {
            WriteLine($" - Name={rule.name}, Limit={(rule.limit ?? 0).ToHumanize()}, {rule.subjects?.Count ?? 0} subjects");
        }
    }
}

/// <summary>group create コマンド</summary>
async ValueTask cmdGroupCreateAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    // ヘルプ表示
    if (arguments.HasToken("--help"))
    {
        WriteLine("group create <name>");
        WriteLine("    name    : 対象グループ名称");
        return;
    }

    // パラメータ取得
    var name = arguments.TakeArgument(out arguments).ToString();

    // 検証
    if (name.IsWhite()) throw new PavedMessageException($"{nameof(name)} は必須です。");

    // 実行・結果表示
    var options = new CreateQuotaGroupOptions(name: name);
    var group = await context.Client.Admin.CreateQuotaGroupAsync(options, context.Breaker);
    WriteLine($"Name={group.name}");
    if (0 < group.rules?.Count)
    {
        WriteLine($"Rules:");
        foreach (var rule in group.rules)
        {
            WriteLine($" - Name={rule.name}, Limit={(rule.limit ?? 0).ToHumanize()}, {rule.subjects?.Count ?? 0} subjects");
        }
    }
}

/// <summary>group delete コマンド</summary>
async ValueTask cmdGroupDeleteAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    // ヘルプ表示
    if (arguments.HasToken("--help"))
    {
        WriteLine("group delete <name>");
        WriteLine("    name    : 対象グループ名称");
        return;
    }

    // パラメータ取得
    var name = arguments.TakeArgument(out arguments).ToString();

    // 検証
    if (name.IsWhite()) throw new PavedMessageException($"{nameof(name)} は必須です。");

    // 実行・結果表示
    await context.Client.Admin.DeleteQuotaGroupAsync(name, context.Breaker);
    WriteLine($"グループ '{name}' を削除しました。");
}

/// <summary>group rule add コマンド</summary>
async ValueTask cmdGroupRuleAddAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    // ヘルプ表示
    if (arguments.HasToken("--help"))
    {
        WriteLine("group rule add <group> <rule>");
        WriteLine("    group : 追加先グループ名");
        WriteLine("    rule  : 追加するルール名");
        return;
    }

    // パラメータ取得
    var group = arguments.TakeArgument(out arguments).ToString();
    var rule = arguments.TakeArgument(out arguments).ToString();

    // 検証
    if (group.IsWhite()) throw new PavedMessageException($"{nameof(group)} は必須です。");
    if (rule.IsWhite()) throw new PavedMessageException($"{nameof(rule)} は必須です。");

    // 情報取得・表示
    await context.Client.Admin.AddQuotaGroupRuleAsync(group, rule, context.Breaker);
    WriteLine($"グループ '{group}' にルール '{rule}' を追加しました。");
}

/// <summary>group rule remove コマンド</summary>
async ValueTask cmdGroupRuleRemoveAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    // ヘルプ表示
    if (arguments.HasToken("--help"))
    {
        WriteLine("group rule remove <group> <rule>");
        WriteLine("    group : 削除対象グループ名");
        WriteLine("    rule  : 削除するルール名");
        return;
    }

    // パラメータ取得
    var group = arguments.TakeArgument(out arguments).ToString();
    var rule = arguments.TakeArgument(out arguments).ToString();

    // 検証
    if (group.IsWhite()) throw new PavedMessageException($"{nameof(group)} は必須です。");
    if (rule.IsWhite()) throw new PavedMessageException($"{nameof(rule)} は必須です。");

    // 情報取得・表示
    await context.Client.Admin.RemoveQuotaGroupRuleAsync(group, rule, context.Breaker);
    WriteLine($"グループ '{group}' からルール '{rule}' を除去しました。");
}

/// <summary>group user list コマンド</summary>
async ValueTask cmdGroupUserListAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    // ヘルプ表示
    if (arguments.HasToken("--help"))
    {
        WriteLine("group user list <group>");
        WriteLine("    group : 対象グループ名");
        return;
    }

    // パラメータ取得
    var group = arguments.TakeArgument(out arguments).ToString();

    // 検証
    if (group.IsWhite()) throw new PavedMessageException($"{nameof(group)} は必須です。");

    // 情報取得・表示
    var users = await context.Client.Admin.ListQuotaGroupUsersAsync(group, context.Breaker);
    if (0 < users?.Length)
    {
        WriteLine($"Users:");
        foreach (var user in users ?? [])
        {
            WriteLine($" - Name={user.login}");
        }
    }
    else
    {
        WriteLine($"ユーザが割り当てられていません");
    }
}

/// <summary>group user add コマンド</summary>
async ValueTask cmdGroupUserAddAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    // ヘルプ表示
    if (arguments.HasToken("--help"))
    {
        WriteLine("group user add <group> <user>");
        WriteLine("    group : 追加先グループ名");
        WriteLine("    user  : 追加するユーザ名");
        return;
    }

    // パラメータ取得
    var group = arguments.TakeArgument(out arguments).ToString();
    var user = arguments.TakeArgument(out arguments).ToString();

    // 検証
    if (group.IsWhite()) throw new PavedMessageException($"{nameof(group)} は必須です。");
    if (user.IsWhite()) throw new PavedMessageException($"{nameof(user)} は必須です。");

    // 情報取得・表示
    await context.Client.Admin.AddQuotaGroupUserAsync(group, user, context.Breaker);
    WriteLine($"グループ '{group}' にユーザ '{user}' を追加しました。");
}

/// <summary>group user remove コマンド</summary>
async ValueTask cmdGroupUserRemoveAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    // ヘルプ表示
    if (arguments.HasToken("--help"))
    {
        WriteLine("group user remove <group> <user>");
        WriteLine("    group : 削除対象グループ名");
        WriteLine("    user  : 削除するユーザ名");
        return;
    }

    // パラメータ取得
    var group = arguments.TakeArgument(out arguments).ToString();
    var user = arguments.TakeArgument(out arguments).ToString();

    // 検証
    if (group.IsWhite()) throw new PavedMessageException($"{nameof(group)} は必須です。");
    if (user.IsWhite()) throw new PavedMessageException($"{nameof(user)} は必須です。");

    // 情報取得・表示
    await context.Client.Admin.RemoveQuotaGroupUserAsync(group, user, context.Breaker);
    WriteLine($"グループ '{group}' からユーザ '{user}' を除去しました。");
}

/// <summary>user info コマンド</summary>
async ValueTask cmdUserInfoAsync(ManageContext context, ReadOnlyMemory<char> arguments)
{
    // ヘルプ表示
    if (arguments.HasToken("--help"))
    {
        WriteLine("user info <user>");
        WriteLine("    user : 対象ユーザ名");
        return;
    }

    // パラメータ取得
    var user = arguments.TakeArgument(out arguments).ToString();

    // 検証
    if (user.IsWhite()) throw new PavedMessageException($"{nameof(user)} は必須です。");

    // 情報取得・表示
    var info = await context.Client.Admin.GetUserQuotaRuleAsync(user, context.Breaker);
    WriteLine($"Used:");
    if (info.used?.size is var used && used != null)
    {
        if (used.assets != null)
        {
            WriteLine($"  Assets:");
            WriteLine($"    Artifacts = {used.assets.artifacts}");
            WriteLine($"    Attachments:");
            if (used.assets.attachments != null)
            {
                WriteLine($"      Issues   = {used.assets.attachments.issues}");
                WriteLine($"      Releases = {used.assets.attachments.releases}");
            }
            WriteLine($"    Packages:");
            if (used.assets.packages != null)
            {
                WriteLine($"      All = {used.assets.packages.all}");
            }
        }
        if (used.git != null)
        {
            WriteLine($"  Git:");
            WriteLine($"    LFS = {used.git.LFS}");
        }
        if (used.repos != null)
        {
            WriteLine($"  Repos:");
            WriteLine($"    Public  = {used.repos.@public}");
            WriteLine($"    Private = {used.repos.@private}");
        }
    }
    if (0 < info.groups?.Length)
    {
        foreach (var group in info.groups)
        {
            WriteLine($"Group: Name={group.name}");
            foreach (var rule in group.rules ?? [])
            {
                WriteLine($" - Rule: Name={rule.name}, Limit={rule.limit}");
            }
        }
    }
}
