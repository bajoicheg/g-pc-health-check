using System.Diagnostics;

namespace G.PcHealthCheck;

internal sealed class DiagnosticBundleForm : Form
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly ComboBox _mode = new() { Name = "BundleMode", DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox _duration = new() { Name = "BundlePerformanceSeconds", DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly ComboBox _interval = new() { Name = "BundlePerformanceInterval", DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly DataGridView _sources = new()
    {
        Name = "BundleSourceGrid", Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false, BackgroundColor = SystemColors.Window
    };
    private readonly Button _start = Button("Собрать пакет", "BundleStart");
    private readonly Button _stop = Button("Остановить", "BundleStop");
    private readonly Button _copy = Button("Копировать сводку", "BundleCopy");
    private readonly Button _save = Button("Сохранить пакет…", "BundleSave");
    private readonly Button _close = Button("Закрыть", "BundleClose");
    private readonly CheckBox _zip = new() { Name = "BundleCreateZip", Text = "Создать ZIP рядом с папкой", Checked = true, AutoSize = true };
    private readonly TextBox _summary = new() { Name = "BundleSummary", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _marker = new() { Name = "BundleMarkerText", Width = 300, MaxLength = 160, PlaceholderText = "Заметка о симптоме (до 160 символов)" };
    private readonly Button _mark = Button("Отметить симптом", "BundleMarker");
    private readonly Label _context = new() { Name = "BundleContext", AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label _status = new() { Name = "BundleStatus", AutoSize = true, Dock = DockStyle.Fill };
    private readonly ProgressBar _progress = new() { Name = "BundleProgress", Style = ProgressBarStyle.Marquee, Width = 110, Visible = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly Stopwatch _elapsed = new();
    private readonly Stopwatch _performanceElapsed = new();
    private readonly List<PerformanceMarker> _pendingMarkers = [];
    private CancellationTokenSource? _cancellation;
    private DiagnosticBundleSnapshot? _current;
    private DiagnosticBundleSnapshot? _lastAttempt;
    private bool _busy;
    private bool _saving;
    private bool _performancePhase;
    private string _stage = "Готово к сбору.";

    public DiagnosticBundleForm()
    {
        Name = "DiagnosticBundleForm";
        Text = "G PC Health Check — пакет для Service Desk";
        Size = new Size(1180, 820); MinimumSize = new Size(900, 650); StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9F);

        _mode.Items.AddRange(["Быстрый", "Расширенный"]); _mode.SelectedIndex = 0;
        _duration.Items.AddRange([30, 60]); _duration.SelectedItem = 60;
        _interval.Items.AddRange([1, 2, 5]); _interval.SelectedItem = 2;

        _sources.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Included", HeaderText = "Включить", Width = 70 });
        _sources.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "Категория", Width = 145, ReadOnly = true });
        _sources.Columns.Add(new DataGridViewTextBoxColumn { Name = "Privacy", HeaderText = "Что собирается / чувствительные данные", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 360, ReadOnly = true });
        _sources.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "Состояние", Width = 150, ReadOnly = true });
        AddSourceRows();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 8 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 8),
            Text = "Единый read-only пакет для Service Desk. Сбор использует права текущего процесса и не запускает исправления, активные сетевые проверки или скрытое повышение. Перед передачей проверьте содержимое: возможны аккаунты/SID, пути и команды процессов, IP/порты, тексты событий, идентификаторы устройств и заметки."
        }, 0, 0);

        var options = Flow();
        options.Controls.AddRange([
            Label("Режим:"), _mode,
            Label("Наблюдение, с:"), _duration,
            Label("Интервал, с:"), _interval,
            _zip
        ]);
        root.Controls.Add(options, 0, 1);
        _context.Text = "Контекст будет зафиксирован при запуске."; root.Controls.Add(_context, 0, 2);
        root.Controls.Add(_sources, 0, 3);

        var tools = Flow(); tools.Controls.AddRange([_start, _stop, _copy, _save, _close, _progress]); root.Controls.Add(tools, 0, 4);
        var markerTools = Flow(); markerTools.Controls.AddRange([_marker, _mark]); root.Controls.Add(markerTools, 0, 5);
        root.Controls.Add(_summary, 0, 6); root.Controls.Add(_status, 0, 7);

        _summary.Text = "Пакет ещё не собран. Выберите режим и категории, затем нажмите «Собрать пакет». Ничего не запускается при открытии окна.";
        _mode.SelectedIndexChanged += (_, _) => ApplyModeDefaults();
        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += (_, _) => RequestStop();
        _copy.Click += (_, _) => { if (_current is { } snapshot) TryUi(() => Clipboard.SetText(DiagnosticBundleReport.Summary(snapshot))); };
        _save.Click += async (_, _) => await SaveAsync();
        _mark.Click += (_, _) => AddMarker();
        _close.Click += (_, _) => Close();
        _timer.Tick += (_, _) => { if (_busy) _status.Text = $"{_stage} · {_elapsed.Elapsed.TotalSeconds:0.0} с"; };
        FormClosing += (_, e) =>
        {
            if (_saving) { e.Cancel = true; _status.Text = "Дождитесь окончания сохранения пакета."; return; }
            _cancellation?.Cancel();
        };
        FormClosed += (_, _) => _timer.Dispose();
        AcceptButton = _start; CancelButton = _close;
        ApplyModeDefaults(); UpdateButtons();
    }

    private void AddSourceRows()
    {
        Add(DiagnosticBundleCategory.Health, "Health Check", "Оценка, coverage, система, аккаунт/SID и предупреждения источников.");
        Add(DiagnosticBundleCategory.Processes, "Процессы", "Имена/PID, пути, командные строки, сеансы и память процессов.");
        Add(DiagnosticBundleCategory.Endpoints, "TCP/UDP", "Локальные/удалённые IP-адреса, порты, PID и процессы.");
        Add(DiagnosticBundleCategory.Events, "События", "Application/System за последний час; тексты событий, Event ID и источники.");
        Add(DiagnosticBundleCategory.Storage, "Накопители", "Модель, DeviceId, прошивка, состояние и доступные reliability counters.");
        Add(DiagnosticBundleCategory.Performance, "Производительность", "CPU/RAM/диск во времени и заметки о симптомах; только Расширенный режим.");
    }

    private void Add(DiagnosticBundleCategory category, string name, string privacy)
    {
        var index = _sources.Rows.Add(false, name, privacy, "Не запрашивается");
        _sources.Rows[index].Tag = category;
    }

    private void ApplyModeDefaults()
    {
        if (_busy) return;
        var mode = CurrentMode(); var defaults = DiagnosticBundleCore.DefaultOptions(mode);
        foreach (DataGridViewRow row in _sources.Rows)
        {
            if (row.Tag is not DiagnosticBundleCategory category) continue;
            var included = row.Cells["Included"];
            included.Value = defaults.Categories.Contains(category);
            included.ReadOnly = category == DiagnosticBundleCategory.Performance;
            row.Cells["State"].Value = defaults.Categories.Contains(category) ? "Готово к запуску" : "Не запрашивается";
        }
        var extended = mode == DiagnosticBundleMode.Extended;
        _duration.Enabled = _interval.Enabled = extended;
        _marker.Enabled = _mark.Enabled = false;
        UpdateButtons();
    }

    private DiagnosticBundleMode CurrentMode() => _mode.SelectedIndex == 1 ? DiagnosticBundleMode.Extended : DiagnosticBundleMode.Quick;

    private DiagnosticBundleOptions BuildOptions()
    {
        _sources.EndEdit();
        var selected = _sources.Rows.Cast<DataGridViewRow>()
            .Where(row => row.Tag is DiagnosticBundleCategory && Convert.ToBoolean(row.Cells["Included"].Value ?? false))
            .Select(row => (DiagnosticBundleCategory)row.Tag!)
            .ToHashSet();
        var options = new DiagnosticBundleOptions(CurrentMode(), selected,
            Convert.ToInt32(_duration.SelectedItem ?? 60), Convert.ToInt32(_interval.SelectedItem ?? 2));
        DiagnosticBundleCore.Validate(options);
        return options;
    }

    private async Task StartAsync()
    {
        if (_busy) return;
        DiagnosticBundleOptions options;
        try { options = BuildOptions(); }
        catch (Exception ex) { _status.Text = ex.Message; return; }

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation; _busy = true; _stage = "Подготовка…"; _elapsed.Restart(); _timer.Start();
        _performanceElapsed.Reset(); _performancePhase = false; _pendingMarkers.Clear();
        var context = ExecutionContextService.Capture();
        _context.Text = ExecutionPolicy.Describe(context);
        FreezeInputs(true); UpdateButtons();

        var progress = new Progress<DiagnosticBundleProgress>(item =>
        {
            if (IsDisposed) return;
            _stage = SourceName(item.Category) + ": " + item.Message;
            SetSourceState(item.Category, item.Phase == "Finished" ? "Завершено" : "Сбор…");
            if (item.Category == DiagnosticBundleCategory.Performance)
            {
                if (item.Phase == "Starting") { _performancePhase = true; _performanceElapsed.Restart(); }
                if (item.Phase == "Finished") { _performancePhase = false; _performanceElapsed.Stop(); }
                UpdateButtons();
            }
        });

        try
        {
            await Gate.WaitAsync(cancellation.Token);
            DiagnosticBundleSnapshot result;
            try
            {
                var service = new DiagnosticBundleService();
                result = await Task.Run(() => service.CollectAsync(options, new WindowsDiagnosticBundleCollector(), context,
                    progress, null, cancellation.Token), cancellation.Token);
            }
            finally { Gate.Release(); }

            if (IsDisposed) return;
            if (result.Performance.Payload is { } performance && _pendingMarkers.Count > 0)
                performance.Markers.AddRange(_pendingMarkers.Take(100));
            _lastAttempt = result;
            RenderSources(result);
            _summary.Text = DiagnosticBundleReport.Summary(result);
            if (result.Sources.Any(source => source.PayloadAvailable)) _current = result;
            else if (_current is not null) _status.Text = "Новая попытка не дала полезного payload; предыдущий сохраняемый пакет оставлен в памяти.";
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed) _status.Text = "Сбор отменён до получения нового полезного результата; предыдущий пакет сохранён в памяти.";
        }
        catch (Exception ex)
        {
            if (!IsDisposed) _status.Text = $"Сбор не завершён: {ex.GetType().Name}; 0x{ex.HResult:X8}. Предыдущий пакет сохранён в памяти.";
        }
        finally
        {
            _cancellation = null; _busy = false; _performancePhase = false; _performanceElapsed.Stop(); _elapsed.Stop();
            if (!IsDisposed) { _timer.Stop(); FreezeInputs(false); UpdateButtons(); }
        }
    }

    private void RequestStop()
    {
        if (_cancellation is not { IsCancellationRequested: false }) return;
        _cancellation.Cancel(); _stage = "Отмена запрошена; поставщик может вернуть управление не сразу."; UpdateButtons();
    }

    private void AddMarker()
    {
        if (!_busy || !_performancePhase || _pendingMarkers.Count >= 100) return;
        var note = _marker.Text.Trim();
        if (note.Length == 0) { _status.Text = "Введите непустую заметку о симптоме."; return; }
        if (note.Length > 160) note = note[..160];
        _pendingMarkers.Add(new PerformanceMarker(_performanceElapsed.ElapsedMilliseconds, note));
        _marker.Clear(); _status.Text = $"Отметка #{_pendingMarkers.Count} добавлена к временной шкале производительности.";
        UpdateButtons();
    }

    private async Task SaveAsync()
    {
        if (_busy || _current is not { } snapshot) return;
        var confirm = MessageBox.Show(this,
            "Пакет может содержать аккаунты/SID, пути и командные строки процессов, IP/порты, тексты событий, идентификаторы устройств и заметки.\n\nПроверьте evidence перед внешней передачей. Поиск/фильтры не являются обезличиванием. Продолжить сохранение?",
            "Конфиденциальность диагностического пакета", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (confirm != DialogResult.OK) return;
        using var dialog = new FolderBrowserDialog
        {
            Description = "Выберите локальную папку для нового диагностического пакета. Существующие пакеты не перезаписываются.",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var request = CreateSaveRequest(snapshot, dialog.SelectedPath);
        _saving = _busy = true; _stage = "Сохраняю пакет…"; _elapsed.Restart(); _timer.Start(); FreezeInputs(true); UpdateButtons();
        try
        {
            var saved = await request.ExecuteAsync();
            if (!IsDisposed) _status.Text = saved.Zip is null ? "Сохранено: " + saved.Folder : $"Сохранено: {saved.Folder}; ZIP: {saved.Zip}";
        }
        catch (Exception ex)
        {
            if (!IsDisposed) MessageBox.Show(this,
                "Сохранение не завершено полностью. Если ошибка возникла при ZIP, уже созданная папка evidence могла сохраниться.\n" + ex.Message,
                "Сохранение пакета", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _saving = _busy = false; _elapsed.Stop(); if (!IsDisposed) { _timer.Stop(); FreezeInputs(false); UpdateButtons(); }
        }
    }

    private DiagnosticBundleSaveRequest CreateSaveRequest(DiagnosticBundleSnapshot snapshot, string parentDirectory)
        => new(snapshot, parentDirectory, _zip.Checked);

    private void RenderSources(DiagnosticBundleSnapshot snapshot)
    {
        foreach (var source in snapshot.Sources) SetSourceState(source.Category, StateText(source.State));
    }

    private void SetSourceState(DiagnosticBundleCategory category, string state)
    {
        var row = _sources.Rows.Cast<DataGridViewRow>().FirstOrDefault(item => Equals(item.Tag, category));
        if (row is not null) row.Cells["State"].Value = state;
    }

    private void FreezeInputs(bool frozen)
    {
        _mode.Enabled = !frozen; _zip.Enabled = !frozen;
        foreach (DataGridViewRow row in _sources.Rows)
        {
            if (row.Tag is not DiagnosticBundleCategory category) continue;
            row.Cells["Included"].ReadOnly = frozen || category == DiagnosticBundleCategory.Performance;
        }
        var extended = CurrentMode() == DiagnosticBundleMode.Extended;
        _duration.Enabled = _interval.Enabled = !frozen && extended;
    }

    private void UpdateButtons()
    {
        if (IsDisposed) return;
        _start.Enabled = !_busy; _stop.Enabled = _busy && !_saving && _cancellation is { IsCancellationRequested: false };
        _copy.Enabled = _save.Enabled = !_busy && _current is not null;
        _progress.Visible = _busy; _mark.Enabled = _marker.Enabled = _busy && _performancePhase && _pendingMarkers.Count < 100;
        if (!_busy && string.IsNullOrWhiteSpace(_status.Text)) _status.Text = _lastAttempt is null ? "Готово к сбору." : "Готово.";
    }

    private static string SourceName(DiagnosticBundleCategory category) => category switch
    {
        DiagnosticBundleCategory.Health => "Health Check", DiagnosticBundleCategory.Processes => "Процессы",
        DiagnosticBundleCategory.Endpoints => "TCP/UDP", DiagnosticBundleCategory.Events => "События",
        DiagnosticBundleCategory.Storage => "Накопители", DiagnosticBundleCategory.Performance => "Производительность",
        _ => category.ToString()
    };

    private static string StateText(string state) => state switch
    {
        "Complete" => "Собрано", "Partial" => "Частично", "Unavailable" => "Недоступно",
        "Cancelled" => "Отменено", "NotRequested" => "Не запрашивается", _ => state
    };

    private static FlowLayoutPanel Flow() => new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
    private static Label Label(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(0, 6, 3, 0) };
    private static Button Button(string text, string name) => new() { Text = text, Name = name, AutoSize = true, Padding = new Padding(5, 2, 5, 2) };
    private void TryUi(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Действие не завершено", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
}

internal static class DiagnosticBundleMenu
{
    public static void Attach(Form main)
    {
        ArgumentNullException.ThrowIfNull(main); ReadOnlyReviewMenu.Attach(main);
        var group = (ToolStripMenuItem)main.MainMenuStrip!.Items.Find("ReadOnlyInspections", false).Single();
        if (group.DropDownItems.Find("DiagnosticBundleOpen", false).Length > 0) return;
        var item = new ToolStripMenuItem("Собрать пакет для Service Desk…") { Name = "DiagnosticBundleOpen" };
        item.Click += (_, _) => { using var window = new DiagnosticBundleForm(); window.ShowDialog(main); };
        group.DropDownItems.Add(item);
    }
}
