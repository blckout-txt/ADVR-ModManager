using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Text;
using Android.Views;
using Android.Widget;
using DanTheMan827.OnDeviceADB;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Uri = Android.Net.Uri;

namespace ADVRModManager;

[Activity(Name = "com.blckout.adv_mod_manager.MainActivity", MainLauncher = true, Exported = true,
    ConfigurationChanges = Android.Content.PM.ConfigChanges.Orientation |
                           Android.Content.PM.ConfigChanges.ScreenSize |
                           Android.Content.PM.ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : Activity
{
    private const string InstallDirectory = "/sdcard/Android/data/de.erthu.ancientdungeonfull/files/ADVR_Mods";
    private const int PickZipRequest = 7321;
    private const string NexusGameDomain = "ancientdungeon";
    private const int StaleAfterMonths = 4;
    private static readonly HttpClient Http = CreateHttpClient();
    private LinearLayout _discoverTab = null!;
    private LinearLayout _installedTab = null!;
    private LinearLayout _settingsTab = null!;
    private LinearLayout _installedResults = null!;
    private LinearLayout _results = null!;
    private EditText _search = null!;
    private EditText _nexusApiKey = null!;
    private TextView _status = null!;
    private EditText _adbHost = null!;
    private EditText _pairingPort = null!;
    private EditText _connectPort = null!;
    private EditText _pairingCode = null!;
    private InstalledMod? _pendingNexusInstall;
    private List<InstalledMod> _installed = new();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        LoadInstalledMetadata();
        BuildUi();
        _ = ReconnectSavedAdbAsync();
    }

    private void BuildUi()
    {
        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };
        page.SetPadding(Dp(20), Dp(18), Dp(20), Dp(20));
        page.SetBackgroundColor(Android.Graphics.Color.Rgb(17, 20, 27));

        var title = new TextView(this) { Text = "ADVR Mod Manager", TextSize = 26, Typeface = Android.Graphics.Typeface.DefaultBold };
        title.SetTextColor(Android.Graphics.Color.White);
        page.AddView(title);
        var subtitle = new TextView(this) { Text = "Discover, install, and manage Ancient Dungeon mods." };
        subtitle.SetTextColor(Android.Graphics.Color.Rgb(175, 184, 199));
        subtitle.SetPadding(0, Dp(4), 0, Dp(16));
        page.AddView(subtitle);

        var tabs = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var discoverButton = new Button(this) { Text = "Discover" };
        var installedButton = new Button(this) { Text = "Installed" };
        var settingsButton = new Button(this) { Text = "Settings" };
        tabs.AddView(discoverButton, new LinearLayout.LayoutParams(0, Dp(46), 1));
        tabs.AddView(installedButton, new LinearLayout.LayoutParams(0, Dp(46), 1));
        tabs.AddView(settingsButton, new LinearLayout.LayoutParams(0, Dp(46), 1));
        page.AddView(tabs);

        var tabHost = new FrameLayout(this);
        _discoverTab = BuildDiscoverTab();
        _installedTab = BuildInstalledTab();
        _settingsTab = BuildSettingsTab();
        _installedTab.Visibility = ViewStates.Gone;
        _settingsTab.Visibility = ViewStates.Gone;
        tabHost.AddView(_discoverTab, new FrameLayout.LayoutParams(-1, -2));
        tabHost.AddView(_installedTab, new FrameLayout.LayoutParams(-1, -2));
        tabHost.AddView(_settingsTab, new FrameLayout.LayoutParams(-1, -2));
        var scroll = new ScrollView(this);
        scroll.AddView(tabHost);
        page.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));

        discoverButton.Click += (_, _) => SetActiveTab(_discoverTab);
        installedButton.Click += async (_, _) => { SetActiveTab(_installedTab); await ShowInstalledModsAsync(); };
        settingsButton.Click += (_, _) => SetActiveTab(_settingsTab);

        _status = new TextView(this) { Text = "Pair using the code in Developer options → Wireless debugging. Pairing and connection ports differ." };
        _status.SetTextColor(Android.Graphics.Color.Rgb(175, 184, 199));
        _status.SetPadding(0, Dp(12), 0, Dp(12));
        page.AddView(_status);
        SetContentView(page);
    }

    private LinearLayout BuildDiscoverTab()
    {
        var tab = new LinearLayout(this) { Orientation = Orientation.Vertical };
        tab.SetPadding(0, Dp(8), 0, 0);
        _search = CreateField("Search mods");
        tab.AddView(_search, new LinearLayout.LayoutParams(-1, Dp(50)));
        var sources = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var thunderstore = new Button(this) { Text = "Thunderstore" };
        thunderstore.Click += async (_, _) => await SearchThunderstoreAsync();
        var nexus = new Button(this) { Text = "Nexus Mods" };
        nexus.Click += async (_, _) => await SearchNexusAsync();
        sources.AddView(thunderstore, new LinearLayout.LayoutParams(0, Dp(48), 1));
        sources.AddView(nexus, new LinearLayout.LayoutParams(0, Dp(48), 1) { LeftMargin = Dp(8) });
        tab.AddView(sources);
        var info = new TextView(this) { Text = "Nexus search needs a personal API key. Add it in Settings.", TextSize = 12 };
        info.SetTextColor(Android.Graphics.Color.Rgb(165, 174, 189));
        tab.AddView(info);
        var import = new Button(this) { Text = "Import ZIP" };
        import.Click += (_, _) => PickZip();
        tab.AddView(import, new LinearLayout.LayoutParams(-1, Dp(46)));
        _results = new LinearLayout(this) { Orientation = Orientation.Vertical };
        tab.AddView(_results);
        return tab;
    }

    private LinearLayout BuildInstalledTab()
    {
        var tab = new LinearLayout(this) { Orientation = Orientation.Vertical };
        tab.SetPadding(0, Dp(8), 0, 0);
        var refresh = new Button(this) { Text = "Refresh installed mods" };
        refresh.Click += async (_, _) => await ShowInstalledModsAsync();
        tab.AddView(refresh, new LinearLayout.LayoutParams(-1, Dp(46)));
        var hint = new TextView(this) { Text = "Delete ZIPs or check source-linked mods for updates.", TextSize = 12 };
        hint.SetTextColor(Android.Graphics.Color.Rgb(165, 174, 189));
        tab.AddView(hint);
        _installedResults = new LinearLayout(this) { Orientation = Orientation.Vertical };
        tab.AddView(_installedResults);
        return tab;
    }

    private LinearLayout BuildSettingsTab()
    {
        var tab = new LinearLayout(this) { Orientation = Orientation.Vertical };
        tab.SetPadding(0, Dp(8), 0, 0);
        AddCardTitle(tab, "Nexus API");
        _nexusApiKey = CreateField("Personal Nexus API key", Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword);
        _nexusApiKey.Text = GetSharedPreferences("advr_mod_manager", FileCreationMode.Private)?.GetString("nexus_api_key", "") ?? "";
        tab.AddView(_nexusApiKey, new LinearLayout.LayoutParams(-1, Dp(46)));
        var keyLink = new Button(this) { Text = "Get a personal Nexus API key" };
        keyLink.Click += (_, _) => StartActivity(new Intent(Intent.ActionView, Uri.Parse("https://www.nexusmods.com/settings/api-keys")));
        tab.AddView(keyLink, new LinearLayout.LayoutParams(-1, Dp(44)));

        AddCardTitle(tab, "On-device ADB");
        var instructions = new TextView(this) { Text = "Pair once using Developer options → Wireless debugging → Pair device with pairing code. The saved ADB connection is reused on later launches; redo pairing here if its address changes." };
        instructions.SetTextColor(Android.Graphics.Color.Rgb(175, 184, 199));
        tab.AddView(instructions);
        _adbHost = CreateField("Headset IP address");
        _adbHost.Text = GetSharedPreferences("advr_mod_manager", FileCreationMode.Private)?.GetString("adb_host", "") ?? "";
        tab.AddView(_adbHost, new LinearLayout.LayoutParams(-1, Dp(46)));
        var ports = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _pairingPort = CreateField("Pairing port", Android.Text.InputTypes.ClassNumber);
        _connectPort = CreateField("Connection port", Android.Text.InputTypes.ClassNumber);
        var savedPort = GetSharedPreferences("advr_mod_manager", FileCreationMode.Private)?.GetInt("adb_connect_port", 0) ?? 0;
        if (savedPort > 0) _connectPort.Text = savedPort.ToString(CultureInfo.InvariantCulture);
        ports.AddView(_pairingPort, new LinearLayout.LayoutParams(0, Dp(46), 1));
        ports.AddView(_connectPort, new LinearLayout.LayoutParams(0, Dp(46), 1) { LeftMargin = Dp(8) });
        tab.AddView(ports);
        _pairingCode = CreateField("6-digit pairing code", Android.Text.InputTypes.ClassNumber);
        tab.AddView(_pairingCode, new LinearLayout.LayoutParams(-1, Dp(46)));
        var pair = new Button(this) { Text = "Pair / reconnect ADB" };
        pair.Click += async (_, _) => await PairAndConnectAsync();
        tab.AddView(pair, new LinearLayout.LayoutParams(-1, Dp(46)));
        var forget = new Button(this) { Text = "Forget saved ADB connection" };
        forget.Click += async (_, _) => await ForgetSavedAdbAsync();
        tab.AddView(forget, new LinearLayout.LayoutParams(-1, Dp(44)));
        return tab;
    }

    private void SetActiveTab(LinearLayout selected)
    {
        _discoverTab.Visibility = selected == _discoverTab ? ViewStates.Visible : ViewStates.Gone;
        _installedTab.Visibility = selected == _installedTab ? ViewStates.Visible : ViewStates.Gone;
        _settingsTab.Visibility = selected == _settingsTab ? ViewStates.Visible : ViewStates.Gone;
    }

    private async Task SearchThunderstoreAsync()
    {
        var query = _search.Text?.Trim() ?? "";
        SetStatus("Searching Thunderstore…");
        _results.RemoveAllViews();
        try
        {
            const string endpoint = "https://thunderstore.io/c/ancient-dungeon-vr/api/v1/package/";
            using var response = await Http.GetAsync(endpoint);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            var packages = new List<ThunderstorePackage>();
            foreach (var item in json.RootElement.EnumerateArray())
            {
                var package = ThunderstorePackage.FromJson(item);
                if (package is not null && (query.Length == 0 || package.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)))
                    packages.Add(package);
            }
            foreach (var package in packages.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Take(100))
                AddPackageCard(package);
            SetStatus(packages.Count == 0 ? "No Thunderstore results." : $"{packages.Count} Thunderstore result(s).");
        }
        catch (Exception ex)
        {
            SetStatus($"Thunderstore search failed: {ex.Message}");
        }
    }

    private void AddPackageCard(ThunderstorePackage package)
    {
        var card = CreateCard();
        AddCardTitle(card, package.Name);
        var detail = new TextView(this) { Text = $"{package.Owner} · {package.LatestVersion}\n{package.Description}" };
        detail.SetTextColor(Android.Graphics.Color.Rgb(190, 198, 210));
        detail.SetMaxLines(3);
        card.AddView(detail);
        var warning = StalenessWarning(package.UpdatedAt);
        if (warning is not null) AddWarning(card, warning);
        var install = new Button(this) { Text = "Download and install" };
        install.Click += async (_, _) => await DownloadAndInstallAsync(package);
        card.AddView(install);
        _results.AddView(card, CardMargins());
    }

    private async Task SearchNexusAsync()
    {
        var query = _search.Text?.Trim() ?? "";
        var apiKey = _nexusApiKey.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetStatus("Enter a personal Nexus API key. Get one at nexusmods.com/settings/api-keys.");
            return;
        }
        SaveNexusApiKey(apiKey);
        _results.RemoveAllViews();
        SetStatus("Searching Nexus Mods…");
        try
        {
            var mods = await QueryNexusModsAsync(apiKey, query);
            foreach (var mod in mods) AddNexusCard(mod);
            SetStatus(mods.Count == 0 ? "No Nexus mods found for Ancient Dungeon." : $"{mods.Count} Nexus result(s). Download a file from Nexus, then import its ZIP here.");
        }
        catch (Exception ex) { SetStatus($"Nexus API search failed: {ex.Message}"); }
    }

    private void AddNexusCard(NexusMod mod)
    {
        var card = CreateCard();
        AddCardTitle(card, mod.Name);
        var detail = new TextView(this) { Text = $"{mod.Author} · {mod.Version}\n{mod.Summary}" };
        detail.SetTextColor(Android.Graphics.Color.Rgb(190, 198, 210));
        detail.SetMaxLines(3);
        card.AddView(detail);
        var warning = StalenessWarning(mod.UpdatedAt);
        if (warning is not null) AddWarning(card, warning);
        var open = new Button(this) { Text = "Open Nexus page" };
        open.Click += (_, _) =>
        {
            _pendingNexusInstall = mod.ToInstalled("");
            OpenNexusPage(mod.ModId);
            SetStatus($"After downloading {mod.Name}, tap Import ZIP to track it for update checks.");
        };
        card.AddView(open);
        _results.AddView(card, CardMargins());
    }

    private async Task<List<NexusMod>> QueryNexusModsAsync(string apiKey, string searchText, int? modId = null)
    {
        var filter = new Dictionary<string, object>
        {
            ["gameDomainName"] = new[] { new { op = "EQUALS", value = NexusGameDomain } }
        };
        if (modId is not null)
            filter["modId"] = new[] { new { op = "EQUALS", value = modId.Value.ToString(CultureInfo.InvariantCulture) } };
        else if (!string.IsNullOrWhiteSpace(searchText))
            filter["name"] = new[] { new { op = "WILDCARD", value = $"*{searchText.Trim()}*" } };

        const string query = "query SearchMods($filter: ModsFilter, $count: Int, $sort: [ModsSort!]) { mods(filter: $filter, count: $count, sort: $sort) { totalCount nodes { modId name summary version updatedAt author game { domainName } } } }";
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.nexusmods.com/v2/graphql");
        request.Headers.Add("apikey", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            query,
            variables = new { filter, count = modId is null ? 30 : 1, sort = new[] { new { relevance = new { direction = "DESC" } } } }
        }), System.Text.Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Nexus returned HTTP {(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        if (json.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
            throw new InvalidOperationException(errors[0].GetProperty("message").GetString() ?? "Nexus GraphQL returned an error.");
        if (!json.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("mods", out var results) || !results.TryGetProperty("nodes", out var nodes))
            throw new InvalidDataException("Nexus returned an unexpected search response.");
        return nodes.EnumerateArray().Select(NexusMod.FromJson).Where(mod => mod is not null).Cast<NexusMod>().ToList();
    }

    private void OpenNexusPage(int modId)
    {
        var url = $"https://www.nexusmods.com/{NexusGameDomain}/mods/{modId}";
        try { StartActivity(new Intent(Intent.ActionView, Uri.Parse(url))); }
        catch (Exception ex) { SetStatus($"Could not open Nexus Mods: {ex.Message}"); }
    }

    private void PickZip()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/zip");
        intent.PutExtra(Intent.ExtraMimeTypes, new[] { "application/zip", "application/x-zip-compressed", "application/octet-stream" });
        StartActivityForResult(intent, PickZipRequest);
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickZipRequest || resultCode != Result.Ok || data?.Data is not Uri uri) return;
        try
        {
            var displayName = GetDisplayName(uri) ?? "mod.zip";
            if (!displayName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose a .zip file.");
            var safeName = SafeFileName(displayName);
            var localPath = Path.Combine(CacheDir!.AbsolutePath, safeName);
            await using (var source = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("Unable to read the selected file."))
            await using (var destination = File.Create(localPath))
                await source.CopyToAsync(destination);
            var targetName = string.IsNullOrEmpty(_pendingNexusInstall?.FileName) ? safeName : _pendingNexusInstall.FileName;
            var metadata = _pendingNexusInstall is null ? null : _pendingNexusInstall with { FileName = targetName };
            await InstallZipAsync(localPath, targetName, metadata);
            _pendingNexusInstall = null;
        }
        catch (Exception ex) { SetStatus($"ZIP import failed: {ex.Message}"); }
    }

    private async Task DownloadAndInstallAsync(ThunderstorePackage package, string? replaceFileName = null)
    {
        var version = package.LatestVersion;
        var outputName = replaceFileName ?? SafeFileName($"{package.Owner}-{package.Name}.zip");
        var localPath = Path.Combine(CacheDir!.AbsolutePath, outputName);
        SetStatus($"Downloading {package.Name}…");
        try
        {
            using var response = await Http.GetAsync(package.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var destination = File.Create(localPath))
                await source.CopyToAsync(destination);
            await InstallZipAsync(localPath, outputName, package.ToInstalled(outputName));
        }
        catch (Exception ex) { SetStatus($"Download or install failed: {ex.Message}"); }
    }

    private async Task InstallZipAsync(string localPath, string fileName, InstalledMod? metadata = null)
    {
        SetStatus("Connecting to OnDeviceADB…");
        var device = await GetAdbDeviceAsync();
        var mkdir = await AdbWrapper.RunShellCommand(device, "mkdir", "-p", InstallDirectory);
        if (mkdir.ExitCode != 0) throw new IOException(mkdir.Error + mkdir.Output);

        var destination = $"{InstallDirectory}/{fileName}";
        var push = await AdbWrapper.RunAdbCommandAsync("-s", device, "push", localPath, destination);
        if (push.ExitCode != 0) throw new IOException(string.IsNullOrWhiteSpace(push.Error) ? push.Output : push.Error);
        _installed.RemoveAll(mod => mod.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        _installed.Add(metadata ?? InstalledMod.Custom(fileName));
        SaveInstalledMetadata();
        SetStatus($"Installed {fileName} to {InstallDirectory}");
    }

    private async Task ShowInstalledModsAsync()
    {
        _installedResults.RemoveAllViews();
        SetStatus("Loading installed ZIPs from Ancient Dungeon…");
        try
        {
            var device = await GetAdbDeviceAsync();
            // RunShellCommand accepts shell arguments; passing a compound `sh -c` command
            // causes the library to split its argument incorrectly (mkdir receives no path).
            var mkdir = await AdbWrapper.RunShellCommand(device, "mkdir", "-p", InstallDirectory);
            if (mkdir.ExitCode != 0) throw new IOException(string.IsNullOrWhiteSpace(mkdir.Error) ? mkdir.Output : mkdir.Error);
            var listing = await AdbWrapper.RunShellCommand(device, "ls", "-1", InstallDirectory);
            if (listing.ExitCode != 0) throw new IOException(string.IsNullOrWhiteSpace(listing.Error) ? listing.Output : listing.Error);
            var files = listing.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(name => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && name == Path.GetFileName(name))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _installed.RemoveAll(mod => !files.Contains(mod.FileName));
            foreach (var fileName in files)
                if (!_installed.Any(mod => mod.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                    _installed.Add(InstalledMod.Custom(fileName));
            SaveInstalledMetadata();
            foreach (var mod in _installed.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)) AddInstalledCard(mod);
            SetStatus(_installed.Count == 0 ? "No ZIP mods installed." : $"{_installed.Count} installed ZIP(s).");
        }
        catch (Exception ex) { SetStatus($"Could not load installed mods: {ex.Message}"); }
    }

    private void AddInstalledCard(InstalledMod mod)
    {
        var card = CreateCard();
        AddCardTitle(card, mod.Name);
        var sourceLabel = mod.Source == "Custom" ? "Custom ZIP" : $"{mod.Source} · {mod.Version}";
        var detail = new TextView(this) { Text = $"{sourceLabel}\n{mod.FileName}" };
        detail.SetTextColor(Android.Graphics.Color.Rgb(190, 198, 210));
        card.AddView(detail);
        var warning = StalenessWarning(mod.UpdatedAt);
        if (warning is not null) AddWarning(card, warning);

        var buttons = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var update = new Button(this) { Text = mod.Source == "Custom" ? "Custom ZIP" : "Check updates" };
        update.Enabled = mod.Source != "Custom";
        ThunderstorePackage? pendingThunderstoreUpdate = null;
        NexusMod? pendingNexusUpdate = null;
        update.Click += async (_, _) =>
        {
            if (pendingThunderstoreUpdate is not null)
            {
                var package = pendingThunderstoreUpdate;
                pendingThunderstoreUpdate = null;
                await DownloadAndInstallAsync(package, mod.FileName);
            }
            else if (pendingNexusUpdate is not null)
            {
                var latest = pendingNexusUpdate;
                pendingNexusUpdate = null;
                _pendingNexusInstall = latest.ToInstalled(mod.FileName);
                OpenNexusPage(latest.ModId);
                SetStatus($"Download the update for {latest.Name} from Nexus, then tap Import ZIP.");
            }
            else
                await CheckUpdatesAsync(mod, update, package => pendingThunderstoreUpdate = package, nexusMod => pendingNexusUpdate = nexusMod);
        };
        var delete = new Button(this) { Text = "Delete" };
        delete.Click += (_, _) => ConfirmDelete(mod);
        buttons.AddView(update, new LinearLayout.LayoutParams(0, Dp(46), 1));
        buttons.AddView(delete, new LinearLayout.LayoutParams(0, Dp(46), 1) { LeftMargin = Dp(8) });
        card.AddView(buttons);
        _installedResults.AddView(card, CardMargins());
    }

    private async Task CheckUpdatesAsync(InstalledMod installed, Button action, Action<ThunderstorePackage> setThunderstoreUpdate, Action<NexusMod> setNexusUpdate)
    {
        action.Enabled = false;
        action.Text = "Checking…";
        try
        {
            if (installed.Source == "Thunderstore")
            {
                var packages = await FetchThunderstorePackagesAsync();
                var package = packages.FirstOrDefault(item => item.Owner.Equals(installed.Owner, StringComparison.OrdinalIgnoreCase) && item.Name.Equals(installed.Name, StringComparison.OrdinalIgnoreCase));
                if (package is null) { action.Text = "Source not found"; return; }
                if (package.LatestVersion.Equals(installed.Version, StringComparison.OrdinalIgnoreCase))
                {
                    action.Text = "Up to date";
                    return;
                }
                action.Text = $"Update · {package.LatestVersion}";
                action.Enabled = true;
                setThunderstoreUpdate(package);
                SetStatus($"Update available for {installed.Name}: {installed.Version} → {package.LatestVersion}.");
                return;
            }

            if (installed.Source == "Nexus")
            {
                var key = _nexusApiKey.Text?.Trim();
                if (string.IsNullOrWhiteSpace(key)) key = GetSharedPreferences("advr_mod_manager", FileCreationMode.Private)?.GetString("nexus_api_key", "");
                if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Enter your Nexus API key in Settings first.");
                var found = await QueryNexusModsAsync(key, "", int.Parse(installed.SourceId, CultureInfo.InvariantCulture));
                var latest = found.FirstOrDefault();
                if (latest is null) { action.Text = "Not found"; return; }
                if (latest.Version.Equals(installed.Version, StringComparison.OrdinalIgnoreCase))
                {
                    action.Text = "Up to date";
                    return;
                }
                action.Text = $"Get update · {latest.Version}";
                action.Enabled = true;
                setNexusUpdate(latest);
                SetStatus($"Nexus update available for {installed.Name}: {installed.Version} → {latest.Version}.");
                return;
            }
        }
        catch (Exception ex)
        {
            action.Text = "Retry update check";
            action.Enabled = true;
            SetStatus($"Update check failed: {ex.Message}");
        }
    }

    private void ConfirmDelete(InstalledMod mod)
    {
        var dialog = new AlertDialog.Builder(this);
        dialog.SetTitle("Delete mod ZIP?");
        dialog.SetMessage($"Remove {mod.FileName} from Ancient Dungeon?");
        dialog.SetNegativeButton("Cancel", (_, _) => { });
        dialog.SetPositiveButton("Delete", async (_, _) => await DeleteInstalledAsync(mod));
        dialog.Show();
    }

    private async Task DeleteInstalledAsync(InstalledMod mod)
    {
        try
        {
            var device = await GetAdbDeviceAsync();
            var path = $"{InstallDirectory}/{mod.FileName}";
            var result = await AdbWrapper.RunShellCommand(device, "rm", "-f", "--", path);
            if (result.ExitCode != 0) throw new IOException(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error);
            _installed.RemoveAll(item => item.FileName.Equals(mod.FileName, StringComparison.OrdinalIgnoreCase));
            SaveInstalledMetadata();
            await ShowInstalledModsAsync();
            SetStatus($"Deleted {mod.FileName}.");
        }
        catch (Exception ex) { SetStatus($"Could not delete ZIP: {ex.Message}"); }
    }

    private async Task<List<ThunderstorePackage>> FetchThunderstorePackagesAsync()
    {
        const string endpoint = "https://thunderstore.io/c/ancient-dungeon-vr/api/v1/package/";
        using var response = await Http.GetAsync(endpoint);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return json.RootElement.EnumerateArray().Select(ThunderstorePackage.FromJson).Where(package => package is not null).Cast<ThunderstorePackage>().ToList();
    }

    private async Task<string> GetAdbDeviceAsync()
    {
        var devices = await AdbWrapper.GetDevicesAsync();
        var device = devices.FirstOrDefault(d => d.Authorized);
        if (device is null)
        {
            var prefs = GetSharedPreferences("advr_mod_manager", FileCreationMode.Private);
            var host = prefs?.GetString("adb_host", "");
            var port = prefs?.GetInt("adb_connect_port", 0) ?? 0;
            if (!string.IsNullOrWhiteSpace(host) && port is > 0 and <= 65535)
            {
                await AdbWrapper.StartServerAsync();
                await AdbWrapper.RunAdbCommandAsync("connect", $"{FormatAdbHost(host)}:{port}");
                device = (await AdbWrapper.GetDevicesAsync()).FirstOrDefault(d => d.Authorized);
            }
        }
        if (device is null)
            throw new InvalidOperationException("No ADB connection. Open Settings and pair the headset again; its Wireless debugging port may have changed.");
        return device.Name;
    }

    private async Task ReconnectSavedAdbAsync()
    {
        var prefs = GetSharedPreferences("advr_mod_manager", FileCreationMode.Private);
        var host = prefs?.GetString("adb_host", "");
        var port = prefs?.GetInt("adb_connect_port", 0) ?? 0;
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
        {
            SetStatus("One-time setup: open Settings to pair the headset over Wireless debugging.");
            return;
        }

        try
        {
            SetStatus("Reconnecting to the saved ADB device…");
            await AdbWrapper.StartServerAsync();
            var endpoint = $"{FormatAdbHost(host)}:{port}";
            var connect = await AdbWrapper.RunAdbCommandAsync("connect", endpoint);
            var device = (await AdbWrapper.GetDevicesAsync()).FirstOrDefault(d => d.Authorized && d.Name.Equals(endpoint, StringComparison.OrdinalIgnoreCase));
            if (device is null)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(connect.Error) ? connect.Output : connect.Error);
            SetStatus($"ADB connected to {device.Name}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Saved ADB connection unavailable: {ex.Message}. Re-pair in Settings if the port changed.");
        }
    }

    private async Task ForgetSavedAdbAsync()
    {
        var prefs = GetSharedPreferences("advr_mod_manager", FileCreationMode.Private);
        var host = prefs?.GetString("adb_host", "");
        var port = prefs?.GetInt("adb_connect_port", 0) ?? 0;
        try
        {
            if (!string.IsNullOrWhiteSpace(host) && port is > 0 and <= 65535)
                await AdbWrapper.DisconnectAsync($"{FormatAdbHost(host)}:{port}");
        }
        catch { /* Forgetting a stale connection should still clear the saved endpoint. */ }
        prefs?.Edit()?.Remove("adb_host")?.Remove("adb_connect_port")?.Apply();
        _adbHost.Text = "";
        _connectPort.Text = "";
        _pairingCode.Text = "";
        SetStatus("Saved ADB connection forgotten. Pair again from Settings when ready.");
    }

    private async Task PairAndConnectAsync()
    {
        var host = _adbHost.Text?.Trim() ?? "";
        var pairingCode = _pairingCode.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(host)) { SetStatus("Enter the IP address shown in the pairing dialog."); return; }
        if (!int.TryParse(_pairingPort.Text, out var pairPort) || pairPort is < 1 or > 65535)
        { SetStatus("Enter the pairing port shown beside the pairing code."); return; }
        if (!int.TryParse(_connectPort.Text, out var connectPort) || connectPort is < 1 or > 65535)
        { SetStatus("Enter the separate port shown on the Wireless debugging screen."); return; }
        if (pairingCode.Length != 6 || pairingCode.Any(c => !char.IsAsciiDigit(c)))
        { SetStatus("The pairing code must contain six digits."); return; }

        try
        {
            SetStatus("Starting OnDeviceADB and pairing…");
            await AdbWrapper.StartServerAsync();
            var endpointHost = FormatAdbHost(host);
            var pair = await AdbWrapper.RunAdbCommandAsync("pair", $"{endpointHost}:{pairPort}", pairingCode);
            if (pair.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(pair.Error) ? pair.Output : pair.Error);

            SetStatus("Paired. Connecting to the Wireless debugging port…");
            var connect = await AdbWrapper.RunAdbCommandAsync("connect", $"{endpointHost}:{connectPort}");
            var devices = await AdbWrapper.GetDevicesAsync();
            var connected = devices.FirstOrDefault(d => d.Authorized && d.Name.EndsWith($":{connectPort}", StringComparison.Ordinal));
            if (connected is null)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(connect.Error) ? connect.Output : connect.Error);

            GetSharedPreferences("advr_mod_manager", FileCreationMode.Private)?.Edit()
                ?.PutString("adb_host", host)?.PutInt("adb_connect_port", connectPort)?.Apply();
            _pairingCode.Text = "";
            SetStatus($"ADB connected to {connected.Name}. Choose a ZIP or install a Thunderstore package.");
        }
        catch (Exception ex)
        {
            SetStatus($"ADB pairing failed: {ex.Message}");
        }
    }

    private EditText CreateField(string hint, Android.Text.InputTypes inputType = Android.Text.InputTypes.ClassText)
    {
        var field = new EditText(this) { Hint = hint, InputType = inputType };
        field.SetSingleLine(true);
        field.SetTextColor(Android.Graphics.Color.White);
        field.SetHintTextColor(Android.Graphics.Color.Rgb(145, 153, 168));
        field.SetTextSize(Android.Util.ComplexUnitType.Sp, 14);
        return field;
    }

    private LinearLayout CreateCard()
    {
        var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
        card.SetPadding(Dp(12), Dp(10), Dp(12), Dp(8));
        card.SetBackgroundColor(Android.Graphics.Color.Rgb(31, 37, 48));
        return card;
    }

    private void AddCardTitle(LinearLayout card, string text)
    {
        var title = new TextView(this) { Text = text, TextSize = 17, Typeface = Android.Graphics.Typeface.DefaultBold };
        title.SetTextColor(Android.Graphics.Color.White);
        card.AddView(title);
    }

    private void AddWarning(LinearLayout card, string message)
    {
        var warning = new TextView(this) { Text = message, TextSize = 13 };
        warning.SetTextColor(Android.Graphics.Color.Rgb(255, 196, 103));
        warning.SetPadding(0, Dp(5), 0, Dp(2));
        card.AddView(warning);
    }

    private LinearLayout.LayoutParams CardMargins() => new(-1, -2) { BottomMargin = Dp(10) };

    private static string? StalenessWarning(DateTimeOffset? updatedAt)
    {
        if (updatedAt is null || updatedAt.Value >= DateTimeOffset.UtcNow.AddMonths(-StaleAfterMonths)) return null;
        return $"⚠ Possibly outdated · last updated {updatedAt.Value.ToLocalTime():MMM yyyy}";
    }

    private void LoadInstalledMetadata()
    {
        var raw = GetSharedPreferences("advr_mod_manager", FileCreationMode.Private)?.GetString("installed_mods", null);
        if (string.IsNullOrWhiteSpace(raw)) return;
        try { _installed = JsonSerializer.Deserialize<List<InstalledMod>>(raw) ?? new List<InstalledMod>(); }
        catch { _installed = new List<InstalledMod>(); }
    }

    private void SaveInstalledMetadata()
    {
        var json = JsonSerializer.Serialize(_installed);
        GetSharedPreferences("advr_mod_manager", FileCreationMode.Private)?.Edit()?.PutString("installed_mods", json)?.Apply();
    }

    private void SaveNexusApiKey(string apiKey) =>
        GetSharedPreferences("advr_mod_manager", FileCreationMode.Private)?.Edit()?.PutString("nexus_api_key", apiKey)?.Apply();

    private static string FormatAdbHost(string host)
    {
        if (host.StartsWith("[") && host.EndsWith("]")) return host;
        return host.Contains(':') ? $"[{host}]" : host;
    }

    private string? GetDisplayName(Uri uri)
    {
        using var cursor = ContentResolver?.Query(uri, null, null, null, null);
        if (cursor is null || !cursor.MoveToFirst()) return null;
        var index = cursor.GetColumnIndex(Android.Provider.IOpenableColumns.DisplayName);
        return index >= 0 ? cursor.GetString(index) : null;
    }

    private void SetStatus(string text) => RunOnUiThread(() => _status.Text = text);
    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);
    private static string SafeFileName(string input)
    {
        var name = Path.GetFileName(input);
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? name : name + ".zip";
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ADVRModManager", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private sealed record ThunderstorePackage(string Owner, string Name, string Description, string LatestVersion, string DownloadUrl, DateTimeOffset? UpdatedAt)
    {
        public string SearchText => $"{Owner} {Name} {Description}";
        public InstalledMod ToInstalled(string fileName) => new(fileName, "Thunderstore", $"{Owner}-{Name}", Owner, Name, LatestVersion, UpdatedAt);

        public static ThunderstorePackage? FromJson(JsonElement item)
        {
            var owner = GetString(item, "owner");
            var name = GetString(item, "name");
            var description = GetString(item, "description");
            if (owner is null || name is null || !item.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
                return null;
            var latest = versions.EnumerateArray().FirstOrDefault();
            if (latest.ValueKind != JsonValueKind.Object) return null;
            var version = GetString(latest, "version_number");
            var download = GetString(latest, "download_url");
            if (version is null || download is null || !System.Uri.TryCreate(download, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != "https") return null;
            var dateText = GetString(item, "date_updated") ?? GetString(latest, "date_created");
            DateTimeOffset? updated = DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
            return new ThunderstorePackage(owner, name, description ?? "", version, download, updated);
        }

        private static string? GetString(JsonElement value, string name) =>
            value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private sealed record NexusMod(int ModId, string Name, string Summary, string Version, string Author, DateTimeOffset? UpdatedAt)
    {
        public InstalledMod ToInstalled(string fileName) => new(fileName, "Nexus", ModId.ToString(CultureInfo.InvariantCulture), Author, Name, Version, UpdatedAt);

        public static NexusMod? FromJson(JsonElement item)
        {
            if (!item.TryGetProperty("modId", out var modId) || !modId.TryGetInt32(out var id)) return null;
            var name = GetString(item, "name");
            var version = GetString(item, "version");
            if (name is null || version is null) return null;
            var dateText = GetString(item, "updatedAt");
            DateTimeOffset? updated = DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
            return new NexusMod(id, name, GetString(item, "summary") ?? "", version, GetString(item, "author") ?? "Nexus Mods", updated);
        }

        private static string? GetString(JsonElement value, string name) =>
            value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private sealed record InstalledMod(string FileName, string Source, string SourceId, string Owner, string Name, string Version, DateTimeOffset? UpdatedAt)
    {
        public static InstalledMod Custom(string fileName)
        {
            var displayName = Path.GetFileNameWithoutExtension(fileName);
            return new InstalledMod(fileName, "Custom", "", "", displayName, "", null);
        }
    }
}
