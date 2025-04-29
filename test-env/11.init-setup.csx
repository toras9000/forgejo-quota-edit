#r "nuget: AngleSharp, 1.3.0"
#r "nuget: Lestaly, 0.76.0"
#nullable enable
using System.Threading;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Io;
using Lestaly;
using Lestaly.Cx;

var settings = new
{
    // サービスのURL
    ServiceURL = "http://localhost:9950",

    // 初回起動セットアップの設定
    Setup = new
    {
        // セットアップ時の admin ユーザ名
        AdminUser = "forgejo-admin",
        // セットアップ時の admin パスワード
        AdminPass = "forgejo-admin-pass",
        // セットアップ時の admin メールアドレス
        AdminMail = "forgejo-admin@example.com",
    },
};

var noInteract = Args.Any(a => a == "--no-interact");
var pauseMode = noInteract ? PavedPause.None : PavedPause.Any;

return await Paved.RunAsync(config: c => c.PauseOn(pauseMode), action: async () =>
{
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);

    // サービスリンクを表示
    WriteLine("Service URL");
    WriteLine($"  {Poster.Link[settings.ServiceURL]}");
    WriteLine();

    // 初回起動(セットアップフォームが表示されるか)を判別する
    WriteLine();
    WriteLine("初期化状態の取得中 ...");
    var requester = new DefaultHttpRequester();
    requester.Timeout = TimeSpan.FromSeconds(3 * 60);
    var config = Configuration.Default.With(requester).WithDefaultLoader();
    var context = BrowsingContext.New(config);
    // ページ取得。なぜか空の内容が得られる場合があるので、空の場合はリトライする
    var document = default(IDocument);
    using (var breaker = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
    {
        while (document == null || document.Source.Length <= 0)
        {
            if (document != null) await Task.Delay(TimeSpan.FromMilliseconds(200), breaker.Token);
            document = await context.OpenAsync(settings.ServiceURL, breaker.Token);
        }
    }
    // 未初期化時に存在する要素の取得を試みる
    var container = document.QuerySelector<IHtmlDivElement>(".install-config-container");
    if (container == null)
    {
        // 初回起動画面を検出できない場合はすでにセットアップ済みと思われるので処理を終える
        WriteLine(".. 既にインスタンスが初期化されている");
        return;
    }
    else
    {
        // 初回起動らしきページを得た場合はセットアップ処理継続
        WriteLine(".. 初期セットアップが必要であることを検出");
    }

    // セットアップフォームの取得を試みる
    WriteLine("初期セットアップを実施中 ...");
    var forms = container.Descendants<IHtmlFormElement>().ToArray();
    if (forms.Length != 1) throw new PavedMessageException("ページ構造が想定外");
    var setupForm = forms[0];
    var inputUpdateChecker = setupForm.QuerySelectorAll<IHtmlInputElement>("*").FirstOrDefault(e => e.Type == "checkbox" && e.Name == "enable_update_checker");
    if (inputUpdateChecker == null) throw new PavedMessageException("Unexpected setup form");
    inputUpdateChecker.IsChecked = false;

    // セットアップパラメータを指定して送信
    var setupResult = await setupForm.SubmitAsync(new
    {
        admin_name = settings.Setup.AdminUser,
        admin_email = settings.Setup.AdminMail,
        admin_passwd = settings.Setup.AdminPass,
        admin_confirm_passwd = settings.Setup.AdminPass,
    });
    var loadingElement = setupResult.GetElementById("goto-user-login");
    if (loadingElement == null) throw new PavedMessageException("ページ構造が想定外");
    WriteLine(".. セットアップ完了");
});
