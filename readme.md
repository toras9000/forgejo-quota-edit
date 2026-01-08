# Forgejo Quota操作補助スクリプト

これは Forgejo v9.0.0 で追加された[ソフトクォータ](https://forgejo.org/docs/latest/admin/quota/)機能をAPI経由で設定参照・操作するスクリプトです。  
厳密には file-based apps 形式のソースコードですが、ここではスクリプトと呼びます。  

## 実行環境

実行には以下の準備が必要となります。

- .NET 10.0 の SDK インストール
    - https://dotnet.microsoft.com/download

また、実行には Forgejo 管理者アカウントのAPIアクセストークンが必要です。  
アクセストークンは `admin` APIルートに読み取りと書き込みの権限を持つ必要があります。  

## ファイル構成

実行するスクリプトファイルは `forgejo-quota-edit.cs1` です。  
その他のスクリプトファイルはそこから利用される補助的なものであり、直接実行するものではありません。  

`test-env` フォルダ配下にはテスト用の docker コンテナ実行用補助ファイル群を格納しています。  
これらはスクリプトの動作を確認するためのテスト環境であり、動作確認以外では特に利用する必要はありません。  

## 実行用の設定

スクリプトは実行対象のサーバに合わせた設定を行う必要があります。  
ファイル `forgejo-quota-edit.cs1` の先頭部にある変数が設定用です。  
`ServiceURL` に対象ForgejoサーバのベースURLまたはAPIベースアドレス(～/api/v1)を指定します。  

## 実行

対象サーバを設定した状態で `forgejo-quota-edit.cs1` を実行します。  
確実な実行方法としては以下のようなコマンドになります。  
```
dotnet run --file ./forgejo-quota-edit.cs1
```
なお、shebang によりファイルが file-based apps であるを認識されるはずなので、`--file` フラグも `run` サブコマンドも無しに実行することも可能なはずです。  

最初の実行ではAPIトークンの入力が求められるので入力します。  
入力したトークンはファイル(デフォルトでは `.auth-forgejo-api`)に簡易スクランブルして保存されます。  
ファイルからトークンを復元できた場合は入力はスキップされます。  
簡易的な物であるため、このファイルはプライベートデータとして管理してください。  

あとは、スクリプトがサポートするコマンドを入力してクォータを操作します。  
`help` や `?` を入力するとサポートするコマンドの一覧を表示します。  
コマンド一覧は階層状に表示しますが、各階層を空白セパレータで連結したものが実行コマンドです。  
たとえば `rule info` や `group user list` のような形です。  

また、具体的な実行コマンドに対して `--help` 引数を与えるとコマンドに与える必要のある引数のヘルプを表示します。  

例：
```
>rule create --help
```

なお、クォータルールの制限対象(subject)には[Quota subjects](https://forgejo.org/docs/latest/admin/config-cheat-sheet/#quota-subjects-list)を指定します。  

## コマンド例

- クォータルール一覧を確認する
    ```
    rule list
    ```
- クォータルールを作成する。
    ```
    rule create package-limit 100M size:assets:packages:all
    ```
    - 名称 `package-limit` で対象 `size:assets:packages:all` に制限サイズ `100M` のルールを作成する。
- クォータグループ一覧を確認する
    ```
    group list
    ```
- クォータグループ一覧を確認する
    ```
    group create general-quota
    ```
    - 名称 `general-quota` のクォータグループを作成する。
- クォータグループにルールを追加する
    ```
    group rule add general-quota package-limit
    ```
    - クォータグループ `general-quota` にルール `package-limit` を追加する。
- クォータグループにユーザを追加する
    ```
    group user add general-quota username
    ```
    - クォータグループ `general-quota` にユーザ `username` を追加する。

