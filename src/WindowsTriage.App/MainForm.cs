using System.Diagnostics;
using WindowsTriage.Core;

namespace WindowsTriage.App;

public sealed class MainForm : Form
{
    private readonly ComboBox _profile = new();
    private readonly CheckBox _includeNetwork = new();
    private readonly CheckBox _includeCommandLines = new();
    private readonly CheckBox _includeMachineName = new();
    private readonly Label _profileDescription = new();
    private readonly Button _startButton = new();
    private readonly Button _openFolderButton = new();
    private readonly Button _copySummaryButton = new();
    private readonly ProgressBar _progress = new();
    private readonly TextBox _status = new();
    private readonly ListView _findings = new();
    private ReportPackage? _lastPackage;

    public MainForm()
    {
        Text = "Windows Triage";
        MinimumSize = new Size(860, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var title = new Label
        {
            Text = "Windows Triage",
            Font = new Font(Font.FontFamily, 20, FontStyle.Bold),
            AutoSize = true
        };
        root.Controls.Add(title);

        var intro = new Label
        {
            Text = "Collect a read-only Windows health report for heat, high CPU, crashes, battery, storage, driver, update, and power issues.",
            AutoSize = true,
            MaximumSize = new Size(780, 0),
            Margin = new Padding(0, 8, 0, 8)
        };
        root.Controls.Add(intro);

        var privacy = new Label
        {
            Text = "Default reports omit machine name, network details, command lines, process paths, and local usernames. Share public_summary.md in public issues.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(780, 0),
            Margin = new Padding(0, 0, 0, 14)
        };
        root.Controls.Add(privacy);

        var options = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 12)
        };
        root.Controls.Add(options);

        options.Controls.Add(new Label { Text = "Scan:", AutoSize = true, Padding = new Padding(0, 7, 4, 0) });
        _profile.DropDownStyle = ComboBoxStyle.DropDownList;
        _profile.Width = 180;
        _profile.Items.AddRange(["Quick (20 sec)", "Full (60 sec)", "Advanced (120 sec)"]);
        _profile.SelectedIndex = 1;
        _profile.SelectedIndexChanged += (_, _) => UpdateProfileDescription();
        options.Controls.Add(_profile);

        _includeNetwork.Text = "Include network details";
        _includeNetwork.AutoSize = true;
        _includeNetwork.Margin = new Padding(18, 6, 0, 0);
        options.Controls.Add(_includeNetwork);

        _includeCommandLines.Text = "Include command lines";
        _includeCommandLines.AutoSize = true;
        _includeCommandLines.Margin = new Padding(18, 6, 0, 0);
        options.Controls.Add(_includeCommandLines);

        _includeMachineName.Text = "Include machine name";
        _includeMachineName.AutoSize = true;
        _includeMachineName.Margin = new Padding(18, 6, 0, 0);
        options.Controls.Add(_includeMachineName);

        _startButton.Text = "Start Scan";
        _startButton.Width = 132;
        _startButton.Height = 34;
        _startButton.Margin = new Padding(18, 0, 0, 0);
        _startButton.Click += async (_, _) => await StartScanAsync().ConfigureAwait(true);
        options.Controls.Add(_startButton);

        _profileDescription.AutoSize = true;
        _profileDescription.ForeColor = SystemColors.GrayText;
        _profileDescription.MaximumSize = new Size(780, 0);
        _profileDescription.Margin = new Padding(0, 0, 0, 10);
        root.Controls.Add(_profileDescription);
        UpdateProfileDescription();

        var body = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 250
        };
        root.Controls.Add(body);

        _findings.Dock = DockStyle.Fill;
        _findings.View = View.Details;
        _findings.FullRowSelect = true;
        _findings.Columns.Add("Severity", 90);
        _findings.Columns.Add("Category", 100);
        _findings.Columns.Add("Finding", 330);
        _findings.Columns.Add("Recommendation", 680);
        body.Panel1.Controls.Add(_findings);

        _status.Dock = DockStyle.Fill;
        _status.Multiline = true;
        _status.ReadOnly = true;
        _status.ScrollBars = ScrollBars.Vertical;
        body.Panel2.Controls.Add(_status);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 12, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(footer);

        _progress.Dock = DockStyle.Fill;
        _progress.Style = ProgressBarStyle.Continuous;
        footer.Controls.Add(_progress, 0, 0);

        _copySummaryButton.Text = "Copy Summary";
        _copySummaryButton.Enabled = false;
        _copySummaryButton.Margin = new Padding(12, 0, 0, 0);
        _copySummaryButton.Click += (_, _) => CopySummary();
        footer.Controls.Add(_copySummaryButton, 1, 0);

        _openFolderButton.Text = "Open Report Folder";
        _openFolderButton.Enabled = false;
        _openFolderButton.Margin = new Padding(12, 0, 0, 0);
        _openFolderButton.Click += (_, _) => OpenReportFolder();
        footer.Controls.Add(_openFolderButton, 2, 0);
    }

    private async Task StartScanAsync()
    {
        _startButton.Enabled = false;
        _openFolderButton.Enabled = false;
        _copySummaryButton.Enabled = false;
        _findings.Items.Clear();
        _status.Clear();
        _progress.Style = ProgressBarStyle.Marquee;

        try
        {
            var runner = new TriageRunner();
            var progress = new Progress<string>(message => AppendStatus(message));
            _lastPackage = await runner.RunAsync(BuildOptions(), progress).ConfigureAwait(true);
            RenderResults(_lastPackage);
            AppendStatus($"Findings: {_lastPackage.Data.Findings.Count}. Collection warnings: {_lastPackage.Data.Warnings.Count}.");
            AppendStatus($"Report: {_lastPackage.TextReportPath}");
            AppendStatus($"Zip: {_lastPackage.ZipPath ?? "not created"}");
            _openFolderButton.Enabled = true;
            _copySummaryButton.Enabled = true;
        }
        catch (Exception ex)
        {
            AppendStatus("Scan failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Windows Triage failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 100;
            _startButton.Enabled = true;
        }
    }

    private CollectionOptions BuildOptions()
    {
        var profile = _profile.SelectedItem?.ToString() switch
        {
            var value when value?.StartsWith("Quick", StringComparison.OrdinalIgnoreCase) == true => ScanProfile.Quick,
            var value when value?.StartsWith("Advanced", StringComparison.OrdinalIgnoreCase) == true => ScanProfile.Advanced,
            _ => ScanProfile.Full
        };

        return new CollectionOptions
        {
            Profile = profile,
            IncludeNetwork = _includeNetwork.Checked,
            IncludeCommandLines = _includeCommandLines.Checked,
            IncludeMachineName = _includeMachineName.Checked
        };
    }

    private void RenderResults(ReportPackage package)
    {
        _findings.Items.Clear();
        foreach (var finding in package.Data.Findings.OrderBy(f => f.Severity).ThenBy(f => f.Category))
        {
            var item = new ListViewItem(finding.Severity.ToString());
            item.SubItems.Add(finding.Category);
            item.SubItems.Add(finding.Title);
            item.SubItems.Add(finding.Recommendation);
            item.Tag = finding;
            _findings.Items.Add(item);
        }
    }

    private void AppendStatus(string message)
    {
        _status.AppendText($"[{DateTime.Now:T}] {message}{Environment.NewLine}");
    }

    private void OpenReportFolder()
    {
        if (_lastPackage is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _lastPackage.ReportFolder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowActionError("Could not open the report folder.", ex);
        }
    }

    private void CopySummary()
    {
        if (_lastPackage is null || !File.Exists(_lastPackage.SummaryPath))
        {
            return;
        }

        try
        {
            Clipboard.SetText(File.ReadAllText(_lastPackage.SummaryPath));
        }
        catch (Exception ex)
        {
            ShowActionError("Could not copy the summary to the clipboard.", ex);
        }
    }

    private void ShowActionError(string message, Exception ex)
    {
        AppendStatus($"{message} {ex.Message}");
        MessageBox.Show(this, $"{message}{Environment.NewLine}{Environment.NewLine}{ex.Message}", "Windows Triage", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void UpdateProfileDescription()
    {
        _profileDescription.Text = _profile.SelectedItem?.ToString() switch
        {
            var value when value?.StartsWith("Quick", StringComparison.OrdinalIgnoreCase) == true =>
                "Quick scan checks the most important signals quickly. Use it when the issue is happening now.",
            var value when value?.StartsWith("Advanced", StringComparison.OrdinalIgnoreCase) == true =>
                "Advanced scan collects deeper power and driver evidence. It takes longer and may produce more supporting files.",
            _ =>
                "Full scan is the recommended default for a balanced health report."
        };
    }
}
