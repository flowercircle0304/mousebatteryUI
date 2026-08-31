namespace MouseBatteryTray;

/// <summary>
/// Lightweight, dependency-free localization: every user-facing string in the app goes through
/// here. Switching language is applied by calling <see cref="SetLanguage"/> once at startup (from
/// <see cref="AppSettings.Language"/>) and again whenever the user changes it in Settings — like
/// every other setting in this app, a language change takes effect the next time a window opens
/// rather than live-updating windows that are already on screen.
/// </summary>
public static class Strings
{
    public static string Lang { get; private set; } = "ja";

    public static void SetLanguage(string lang) => Lang = lang == "en" ? "en" : "ja";

    private static string P(string ja, string en) => Lang == "en" ? en : ja;

    // ===== Tray / notifications =====
    public static string TrayScanning => P("マウスバッテリー: スキャン中...", "Mouse Battery: scanning...");
    public static string TrayNoDevices => P("マウスバッテリー: 対応デバイス未検出", "Mouse Battery: no supported device found");
    public static string TrayLineWaiting(string label) => P($"{label}: 応答待ち...", $"{label}: waiting...");
    public static string TrayLineReading(string label, int percent, bool charging) =>
        P($"{label}: {percent}%{(charging ? " (充電中)" : "")}", $"{label}: {percent}%{(charging ? " (charging)" : "")}");

    public static string AppName => "Mouse Battery Tray";
    public static string CompanionLaunchFailed(string path) =>
        P($"連携ソフトを起動できませんでした:\n{path}", $"Couldn't launch the companion app:\n{path}");

    public static string LowBatteryTitle => P("バッテリー残量が低下しています", "Battery is running low");
    public static string FullChargeTitle => P("充電完了", "Fully charged");
    public static string BalloonRemaining(string label, int percent) => P($"{label}: 残り{percent}%", $"{label}: {percent}% remaining");

    public static string UpdateAvailableTitle => P("新しいバージョンがあります", "A new version is available");
    public static string UpdateAvailableText(string version) =>
        P($"v{version} が利用可能です。クリックでダウンロードページを開きます。", $"v{version} is available. Click to open the download page.");

    // ===== Popup panel =====
    public static string PopupTitle => "MOUSE  BATTERY";
    public static string PopupNoDevices => P("対応デバイスが見つかりません", "No supported device found");
    public static string PopupCharging => P("⚡充電中", "⚡ Charging");
    public static string PopupWaiting => P("応答待ち", "Waiting...");
    public static string PopupClickToLaunch => P("クリックで起動 ▷", "Click to launch ▷");
    public static string PopupRefresh => P("今すぐ更新", "Refresh now");
    public static string PopupExit => P("終了", "Exit");
    public static string PopupEtaPrefix => P("残り約", "~");
    public static string PopupEtaHours(int hours) => P($"{hours}時間", $"{hours}h");
    public static string PopupEtaMinutes(int minutes) => P($"{minutes}分", $"{minutes}m");

    // ===== Settings window =====
    public static string SettingsTitle => P("マウスバッテリー設定", "Mouse Battery Settings");
    public static string SettingsSectionGeneral => P("全般", "General");
    public static string SettingsAutoStart => P("Windows ログイン時に自動起動する", "Start automatically when Windows logs in");
    public static string SettingsAutoUpdateCheck => P("新しいバージョンを自動でチェックする", "Automatically check for new versions");
    public static string SettingsLowBatteryPrefix => P("バッテリー残量が下がったら通知する（しきい値：", "Notify when battery gets low (threshold: ");
    public static string SettingsLowBatterySuffix => P("% 以下）", "% or below)");
    public static string SettingsFullChargePrefix => P("満充電に近づいたら通知する（しきい値：", "Notify when nearly fully charged (threshold: ");
    public static string SettingsFullChargeSuffix => P("% 以上）", "% or above)");
    public static string SettingsExport => P("設定をエクスポート...", "Export settings...");
    public static string SettingsImport => P("設定をインポート...", "Import settings...");
    public static string SettingsLanguage => P("言語", "Language");

    public static string SettingsSectionDevices => P("対応マウス", "Supported mice");
    public static string SettingsDeviceHint => P(
        "チェックを外すとそのマウスの監視を停止します。連携ソフトのパス(または URL)を登録すると、\nポップアップでそのデバイスをクリックしたときに起動できます。",
        "Uncheck to stop monitoring that mouse. Register a companion app's path (or a URL) to\nlaunch it when you click that device's card in the popup.");
    public static string SettingsAddMouse => P("＋ 新しいマウスを追加...", "＋ Add a new mouse...");
    public static string SettingsBrowse => P("参照...", "Browse...");
    public static string SettingsCompanionPlaceholder => P("未設定（クリックしても何も起きません）", "Not set (clicking does nothing)");
    public static string SettingsChooseCompanionTitle(string deviceName) =>
        P($"{deviceName} の連携ソフトを選択", $"Choose a companion app for {deviceName}");
    public static string SettingsDelete => P("削除", "Delete");
    public static string SettingsUnhideLink(int count) => P($"非表示にした {count} 件を再表示する", $"Show {count} hidden device(s) again");
    public static string SettingsSave => P("保存", "Save");
    public static string SettingsCancel => P("キャンセル", "Cancel");

    public static string ConfirmDeleteCustomTitle => P("削除の確認", "Confirm delete");
    public static string ConfirmDeleteCustomText(string name) => P(
        $"「{name}」をウィザードで追加した一覧から削除しますか？\n（既定で対応しているマウスには影響しません）",
        $"Delete \"{name}\" from the list added by the wizard?\n(This doesn't affect the mice supported out of the box.)");
    public static string ConfirmHideBuiltInTitle => P("非表示の確認", "Confirm hide");
    public static string ConfirmHideBuiltInText(string name) => P(
        $"「{name}」を一覧から非表示にしますか？\n監視も停止します。後から「非表示にしたマウスを再表示する」でいつでも元に戻せます。",
        $"Hide \"{name}\" from the list?\nMonitoring will stop too. You can always bring it back via \"Show hidden device(s) again\".");
    public static string NoticeHidden => P("非表示にしました。反映するには一度この設定画面を閉じて開き直してください。", "Hidden. Close and reopen this Settings window to apply it.");
    public static string NoticeDeleted => P("削除しました。反映するには一度この設定画面を閉じて開き直してください。", "Deleted. Close and reopen this Settings window to apply it.");
    public static string NoticeUnhidden => P("再表示しました。反映するには一度この設定画面を閉じて開き直してください。", "Unhidden. Close and reopen this Settings window to apply it.");
    public static string NoticeAdded => P("マウスを追加しました。反映するには一度この設定画面を閉じて開き直してください。", "Mouse added. Close and reopen this Settings window to apply it.");
    public static string NoticeImported => P("インポートしました。反映するには一度この設定画面を閉じて開き直してください。", "Imported. Close and reopen this Settings window to apply it.");

    public static string JsonFileFilter => P("設定ファイル (*.json)|*.json", "Settings file (*.json)|*.json");
    public static string ExportDialogTitle => P("設定をエクスポート", "Export settings");
    public static string ImportDialogTitle => P("設定をインポート", "Import settings");
    public static string ExportSucceeded => P("エクスポートしました。", "Exported.");
    public static string ExportFailed(string message) => P($"エクスポートに失敗しました:\n{message}", $"Export failed:\n{message}");
    public static string ImportReadFailed => P("ファイルを読み込めませんでした。形式を確認してください。", "Couldn't read the file. Check that it's a valid settings export.");
    public static string ImportConfirmText => P("現在の設定を上書きします。よろしいですか？", "This will overwrite your current settings. Continue?");
    public static string ImportConfirmTitle => P("インポートの確認", "Confirm import");
    public static string ExeFileFilter => P("実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*", "Executable (*.exe)|*.exe|All files (*.*)|*.*");

    // ===== Add-mouse wizard =====
    public static string WizardTitle => P("新しいマウスを追加", "Add a new mouse");
    public static string WizardTargetDevice => P("対象デバイス", "Target device");
    public static string WizardRescan => P("再スキャン", "Rescan");
    public static string WizardDeviceHint => P(
        "対応済みでないマウスの受信機だけが一覧に出ます。見当たらない場合は挿し直して「再スキャン」してください。",
        "Only receivers that aren't already supported show up here. If yours is missing, reconnect it and click \"Rescan\".");
    public static string WizardPercentLabel => P(
        "現在のバッテリー%（付属の公式ソフトや本体表示で確認）：",
        "Current battery % (check the vendor's app or the mouse's own display):");
    public static string WizardScanButton => P("スキャン開始（受信待ち・安全）", "Start scan (listen only, safe)");
    public static string WizardActiveScanButton => P("アクティブ探索も試す（診断コマンド送信）", "Also try active probing (sends a diagnostic command)");
    public static string WizardNameLabel => P("表示名：", "Display name:");
    public static string WizardSaveButton => P("この設定を保存", "Save this device");
    public static string WizardClose => P("閉じる", "Close");

    public static string WizardUnknownDevice(int vid, int pid) => P($"不明なデバイス (VID_{vid:X4}&PID_{pid:X4})", $"Unknown device (VID_{vid:X4}&PID_{pid:X4})");
    public static string WizardSelectDevice => P("デバイスを選択してください。", "Please select a device.");
    public static string WizardPassiveScanHeader(string device, int target) => P($"[受信待ちスキャン] {device} / 目標値 {target}%", $"[Passive scan] {device} / target {target}%");
    public static string WizardMoveMouseHint => P(
        "マウスを軽く動かすかクリックすると受信間隔が早まることがあります。",
        "Moving or clicking the mouse can make it report sooner.");
    public static string WizardPassiveNotFound => P("受信待ちスキャンでは見つかりませんでした。", "Not found via passive scan.");
    public static string WizardFoundPassive => P("✓ 見つかりました（受信待ち方式）", "✓ Found it (passive push)");
    public static string WizardActiveScanHeader(string device, int target) => P($"[アクティブ探索] {device} / 目標値 {target}%", $"[Active probe] {device} / target {target}%");
    public static string WizardActiveWakeHint => P(
        "マウスがスリープ状態だと応答しないことがあります。軽く動かしてから実行してください。",
        "The mouse may not respond while asleep — move it a little before running this.");
    public static string WizardActiveConfirmTitle => P("アクティブ探索の確認", "Confirm active probing");
    public static string WizardActiveConfirmText => P(
        "デバイスに1件だけ診断コマンド（バッテリー残量取得用、既知の安全なコマンド）を送信します。\n" +
        "通常は問題ありませんが、対応していないデバイスの場合は無視されるか、想定外の反応をする可能性があります。続行しますか？",
        "This sends exactly one diagnostic command to the device (a known-safe battery-level query).\n" +
        "It's normally harmless, but an unsupported device might ignore it or react unexpectedly. Continue?");
    public static string WizardFoundActive => P("✓ 見つかりました（COMPX方式）", "✓ Found it (COMPX protocol)");
    public static string WizardNotFoundActive => P("✗ 自動では見つかりませんでした。手動解析が必要です。", "✗ Couldn't find it automatically — manual analysis is needed.");

    // ===== Device discovery log lines =====
    public static string DiscoveryNoResponse(int reportLen) => P($"受信レポート長{reportLen}: 応答なし（スキップ）", $"Report length {reportLen}: no response (skipped)");
    public static string DiscoveryPassiveMatch(int reportLen, int offset) => P($"一致しました: 受信レポート長{reportLen} / オフセット{offset}", $"Match found: report length {reportLen} / offset {offset}");
    public static string DiscoveryNoMatch(int reportLen, int samples) => P($"受信レポート長{reportLen}: {samples}件受信、一致バイトなし", $"Report length {reportLen}: {samples} sample(s) received, no matching byte");
    public static string DiscoveryNoCompxCollection => P("COMPX形式（17バイト入出力）のコレクションが見つかりません", "No COMPX-shaped (17-byte in/out) collection found");
    public static string DiscoveryActiveMatch => P("一致しました: COMPX方式（ReportId=8, commandId=4, オフセット6）", "Match found: COMPX protocol (ReportId=8, commandId=4, offset=6)");
    public static string DiscoveryActiveNoMatch => P("応答はありましたが値が一致しませんでした", "Got a response but the value didn't match");
    public static string DiscoveryError(string kind) => P($"エラー: {kind}", $"Error: {kind}");
}
