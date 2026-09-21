# Release手順

1. `CodexMeter.csproj` のVersionを更新し、READMEとRELEASE-NOTES.mdを確認します。
2. `Build.ps1` と実行ファイルの `--self-test` を実行します。
3. `Build-Installer.ps1` で署名なしのWindows x64インストーラーを作成します。
4. インストール、起動、期限表示、終了、既存通知済み状態の保持を確認します。
5. 変更をコミットし、Versionに一致する `v1.0.0` 形式のタグをpushします。
6. Actionsが作成したドラフトReleaseで、CodexMeter-Setup.exeとSHA256SUMS.txtを確認して公開します。
7. 公開Releaseから再ダウンロードし、SHA-256一致を確認します。
8. `/releases/latest/download/CodexMeter-Setup.exe` の実ダウンロード成功を確認した後、HPカードのstatusをavailableへ変更して公開します。

GitHubリポジトリはCodex MeterとHPで分離します。CIのcontents:writeはRelease作成ジョブにだけ付与します。
Actions内のGitHub CLIはGitHubが発行する実行時トークンを使用します。個人トークンをソースへ保存しません。
`.tools/`、`.build/`、`.nuget/`、`artifacts/`、`bin/`、`obj/`、`state/`、`PROJECT_STATE.md` は公開ソースの対象外です。
ライセンス未設定の状態でOSSライセンスを表示したり、署名済みと案内したりしないでください。
