using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class Settings
{
    public string Url = "";
    public string Token = "";
    public string First = "";
    public string Second = "";
    public int Color = System.Drawing.Color.Gold.ToArgb();
}

class Sensor
{
    public string entity_id { get; set; }
    public string state { get; set; }
    public Attributes attributes { get; set; }
}

class Attributes
{
    public string friendly_name { get; set; }
    public string unit_of_measurement { get; set; }
}

static class Solar
{
    public const double Capacity = 4400;
    public static readonly CultureInfo Locale = CultureInfo.GetCultureInfo("pt-BR");
    static readonly HttpClient Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(6), MaxResponseContentBufferSize = 16 * 1024 * 1024 };

    public static string Format(double watts)
    {
        return String.Format(Locale, "{0:N0} W  ·  {1:N1}%", watts, watts / Capacity * 100);
    }

    public static void Validate(Settings s)
    {
        Uri uri;
        if (!Uri.TryCreate(s.Url, UriKind.Absolute, out uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") ||
            uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "")
            throw new InvalidOperationException("Informe o endereço HTTP ou HTTPS do Home Assistant.");
        if (s.Token.Length == 0 || s.Token.Any(Char.IsWhiteSpace))
            throw new InvalidOperationException("Informe um token válido do Home Assistant.");
        if ((s.First != "" && !Regex.IsMatch(s.First, @"\Asensor\.[a-z0-9_]+\z")) ||
            (s.Second != "" && (!Regex.IsMatch(s.Second, @"\Asensor\.[a-z0-9_]+\z") || s.First == "" || s.First == s.Second)))
            throw new InvalidOperationException("Informe IDs de sensores válidos e distintos.");
    }

    public static bool IsTotal(Sensor sensor)
    {
        return sensor != null && sensor.entity_id != null &&
            Regex.IsMatch(sensor.entity_id, @"\Asensor\.[a-z0-9_]+\z") &&
            (sensor.entity_id == "sensor.potencia_solar_total" ||
                String.Equals(sensor.attributes == null ? null : sensor.attributes.friendly_name,
                    "Potência Solar Total", StringComparison.OrdinalIgnoreCase));
    }

    public static double Watts(Sensor sensor)
    {
        double value;
        string unit = sensor.attributes == null ? null : sensor.attributes.unit_of_measurement;
        if (unit != "W" && unit != "kW")
            throw new InvalidOperationException("O sensor precisa medir potência em W ou kW.");
        if (!Double.TryParse(sensor.state, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            throw new InvalidOperationException("Leitura solar indisponível.");
        value *= unit == "kW" ? 1000 : 1;
        if (Double.IsNaN(value) || Double.IsInfinity(value) || value < 0)
            throw new InvalidOperationException("Leitura solar indisponível.");
        return value;
    }

    static async Task<T> Get<T>(Settings s, string id)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Get, s.Url + "/api/states" + id))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Token);
            using (var response = await Client.SendAsync(request).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    throw new InvalidOperationException("Acesso negado. Confira o token.");
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new InvalidOperationException("Sensor ou endereço do Home Assistant não encontrado.");
                response.EnsureSuccessStatusCode();
                var json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
                return json.Deserialize<T>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }
    }

    public static async Task<double> Read(Settings s, string[] detected)
    {
        if (s.First == "" && detected[0] == null)
        {
            var sensors = (await Get<Sensor[]>(s, "").ConfigureAwait(false)).Where(IsTotal).ToArray();
            if (sensors.Length != 1)
                throw new InvalidOperationException("Informe o ID do sensor total ou os IDs dos dois inversores.");
            var watts = Watts(sensors[0]);
            detected[0] = sensors[0].entity_id;
            return watts;
        }
        string first = "/" + (s.First == "" ? detected[0] : s.First);
        if (s.Second == "") return Watts(await Get<Sensor>(s, first).ConfigureAwait(false));
        var readings = await Task.WhenAll(Get<Sensor>(s, first), Get<Sensor>(s, "/" + s.Second)).ConfigureAwait(false);
        var total = readings.Sum(Watts);
        if (Double.IsInfinity(total)) throw new InvalidOperationException("Leitura solar indisponível.");
        return total;
    }
}

sealed class TaskbarText : Form
{
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

    public readonly Label Readout = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };

    public TaskbarText()
    {
        ShowInTaskbar = false;
        TopMost = true;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(32, 34, 39);
        Font = new Font("Segoe UI", 10);
        Controls.Add(Readout);
    }

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; }
    }

    public bool PositionOnTaskbar()
    {
        IntPtr bar = FindWindow("Shell_TrayWnd", null);
        IntPtr tray = FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);
        Rect b, t;
        if (bar == IntPtr.Zero || tray == IntPtr.Zero || !IsWindowVisible(bar) ||
            !GetWindowRect(bar, out b) || !GetWindowRect(tray, out t)) { Hide(); return false; }
        var screen = Screen.FromHandle(bar).Bounds;
        var height = b.Bottom - b.Top;
        var width = Math.Max(TextRenderer.MeasureText("Solar indisponível", Font).Width,
            TextRenderer.MeasureText(Readout.Text, Font).Width) + 14;
        if (height < 20 || b.Top >= screen.Bottom - 3 || t.Left - b.Left < width + 20)
        { Hide(); return false; }
        var x = t.Left - width - 8;
        var y = b.Top + 2;
        var bounds = new Rectangle(x, y, width, height - 4);
        if (Bounds != bounds) Bounds = bounds;
        if (!Visible)
        {
            try
            {
                using (var pixel = new Bitmap(1, 1))
                {
                    using (var g = Graphics.FromImage(pixel)) g.CopyFromScreen(x, b.Top + 1, 0, 0, new Size(1, 1));
                    BackColor = pixel.GetPixel(0, 0);
                }
            }
            catch (ExternalException) { BackColor = Color.FromArgb(32, 34, 39); }
            Show();
        }
        SetWindowPos(Handle, new IntPtr(-1), x, y, width, height - 4, 0x10 | 0x40);
        return true;
    }
}

sealed class Monitor : ApplicationContext
{
    static readonly Tuple<string, Color>[] Palette = {
        Tuple.Create("Roxo", Color.FromArgb(155, 89, 255)),
        Tuple.Create("Azul", Color.RoyalBlue),
        Tuple.Create("Ciano", Color.Turquoise),
        Tuple.Create("Verde", Color.LimeGreen),
        Tuple.Create("Amarelo", Color.Gold),
        Tuple.Create("Laranja", Color.DarkOrange),
        Tuple.Create("Vermelho", Color.Crimson),
        Tuple.Create("Rosa", Color.HotPink),
        Tuple.Create("Branco", Color.White),
        Tuple.Create("Preto", Color.Black)
    };

    static readonly string Profile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorSolar");
    static readonly string Config = Path.Combine(Profile, "settings.dat");
    readonly TaskbarText widget = new TaskbarText();
    readonly NotifyIcon tray = new NotifyIcon { Icon = SystemIcons.Information };
    readonly System.Windows.Forms.Timer position = new System.Windows.Forms.Timer { Interval = 1000 };
    readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 15000 };
    readonly ToolTip hint = new ToolTip();
    Settings settings = new Settings();
    string[] detected = new string[1];
    bool busy, closed;
    string detail = "Configure a conexão com o Home Assistant.";

    public Monitor()
    {
        try
        {
            if (File.Exists(Config))
                settings = new JavaScriptSerializer().Deserialize<Settings>(Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(File.ReadAllBytes(Config), null, DataProtectionScope.CurrentUser)));
        }
        catch (Exception) { detail = "Abra Configurar para restaurar a conexão com o Home Assistant."; }
        var menu = new ContextMenuStrip();
        menu.Items.Add("Configurar", null, (s, e) => Configure());
        var colors = new ToolStripMenuItem("Trocar cor");
        foreach (var preset in Palette) AddColor(colors, preset.Item1, preset.Item2);
        colors.DropDownItems.Add(new ToolStripSeparator());
        colors.DropDownItems.Add("Outra cor…", null, (s, e) => ChooseColor());
        menu.Items.Add(colors);
        menu.Items.Add("Atualizar", null, (s, e) => Read());
        menu.Items.Add("Sair", null, (s, e) => ExitThread());
        widget.Readout.ContextMenuStrip = menu;
        widget.Readout.Click += (s, e) => Configure();
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Configure(); };
        position.Tick += (s, e) => Position();
        refresh.Tick += (s, e) => Read();
        SetText("Configurar solar", detail);
        position.Start();
        var start = new System.Windows.Forms.Timer { Interval = 50 };
        start.Tick += (s, e) =>
        {
            start.Dispose();
            Position();
            if (settings.Url == "") Configure();
            else Read();
        };
        start.Start();
    }

    void Position()
    {
        bool useTray = !widget.PositionOnTaskbar();
        if (tray.Visible != useTray) tray.Visible = useTray;
    }

    void SetText(string text, string status)
    {
        detail = status;
        widget.Readout.ForeColor = Color.FromArgb(settings.Color);
        widget.Readout.Text = text;
        hint.SetToolTip(widget.Readout, status);
        tray.Text = text;
    }

    async void Read()
    {
        if (busy || closed || settings.Url == "") return;
        refresh.Stop();
        busy = true;
        var current = settings;
        try
        {
            double watts = await Solar.Read(current, detected);
            if (!closed && current == settings) SetText(Solar.Format(watts), "Potência solar total · 100% = 4.400 W");
        }
        catch (Exception ex)
        {
            if (!closed && current == settings)
                SetText("Solar indisponível", ex is InvalidOperationException ? ex.Message : "Falha ao consultar o Home Assistant.");
        }
        finally
        {
            busy = false;
            if (!closed)
            {
                if (current != settings) Read();
                else refresh.Start();
            }
        }
    }

    void Save(Settings next)
    {
        Directory.CreateDirectory(Profile);
        var data = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(next));
        var temp = Config + ".tmp";
        File.WriteAllBytes(temp, ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser));
        if (File.Exists(Config)) File.Replace(temp, Config, null);
        else File.Move(temp, Config);
    }

    void AddColor(ToolStripMenuItem menu, string label, Color color)
    {
        var swatch = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(swatch))
        {
            graphics.Clear(color);
            graphics.DrawRectangle(SystemPens.ControlDark, 0, 0, 15, 15);
        }
        var item = new ToolStripMenuItem(label, swatch, (s, e) => ApplyColor(color));
        menu.DropDownOpening += (s, e) => item.Checked = settings.Color == color.ToArgb();
        item.Disposed += (s, e) => swatch.Dispose();
        menu.DropDownItems.Add(item);
    }

    void ChooseColor()
    {
        using (var dialog = new ColorDialog { Color = Color.FromArgb(settings.Color), FullOpen = true })
        {
            if (dialog.ShowDialog() == DialogResult.OK) ApplyColor(dialog.Color);
        }
    }

    void ApplyColor(Color color)
    {
        int previous = settings.Color;
        settings.Color = color.ToArgb();
        try { Save(settings); }
        catch (Exception)
        {
            settings.Color = previous;
            MessageBox.Show("Não foi possível salvar a cor.", "Monitor Solar");
        }
        SetText(widget.Readout.Text, detail);
    }

    void Configure()
    {
        using (var form = new Form { Text = "Monitor Solar · Home Assistant", StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(520, 430), FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, Font = new Font("Segoe UI", 10) })
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, AutoScroll = true };
            form.Controls.Add(layout);
            var status = new Label { Text = detail, AutoSize = true, MaximumSize = new Size(480, 0) };
            layout.Controls.Add(status);
            var url = Field(layout, "Endereço do Home Assistant", settings.Url);
            var token = Field(layout, "Token de acesso de longa duração", settings.Token);
            token.UseSystemPasswordChar = true;
            var first = Field(layout, "Sensor total ou inversor 1 (ID)", settings.First);
            var second = Field(layout, "Inversor 2 (ID, opcional)", settings.Second);
            layout.Controls.Add(new Label { Text = "Campos de sensor vazios procuram “Potência Solar Total”.", AutoSize = true });
            layout.Controls.Add(new Label { Text = "Cor do indicador", AutoSize = true });
            var colors = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            colors.Items.AddRange(Palette.Select(preset => preset.Item1).ToArray());
            colors.Items.Add("Personalizada");
            var chosenColor = Color.FromArgb(settings.Color);
            int selected = Array.FindIndex(Palette, preset => preset.Item2.ToArgb() == chosenColor.ToArgb());
            colors.SelectedIndex = selected >= 0 ? selected : Palette.Length;
            var customColor = chosenColor;
            var preview = new Label { Text = "■", AutoSize = true, ForeColor = chosenColor };
            colors.SelectedIndexChanged += (sender, args) =>
            {
                if (colors.SelectedIndex < 0) return;
                chosenColor = colors.SelectedIndex < Palette.Length
                    ? Palette[colors.SelectedIndex].Item2 : customColor;
                preview.ForeColor = chosenColor;
            };
            var custom = new Button { Text = "Outra cor…", AutoSize = true };
            custom.Click += (sender, args) =>
            {
                using (var dialog = new ColorDialog { Color = chosenColor, FullOpen = true })
                {
                    if (dialog.ShowDialog(form) != DialogResult.OK) return;
                    customColor = dialog.Color;
                    colors.SelectedIndex = Palette.Length;
                    chosenColor = customColor;
                    preview.ForeColor = chosenColor;
                }
            };
            var colorRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            colorRow.Controls.AddRange(new Control[] { colors, custom, preview });
            layout.Controls.Add(colorRow);
            var save = new Button { Text = "Salvar e atualizar", AutoSize = true };
            layout.Controls.Add(save);
            form.AcceptButton = save;
            save.Click += (sender, args) =>
            {
                var next = new Settings { Url = url.Text.Trim().TrimEnd('/'), Token = token.Text.Trim(),
                    First = first.Text.Trim(), Second = second.Text.Trim(), Color = chosenColor.ToArgb() };
                try
                {
                    if (next.Url != "" || next.Token != "" || next.First != "" || next.Second != "")
                        Solar.Validate(next);
                    Save(next);
                    settings = next;
                    detected = new string[1];
                    if (next.Url == "") SetText("Configurar solar", "Configure a conexão com o Home Assistant.");
                    else SetText("Lendo geração…", "Consultando o Home Assistant.");
                    form.Close();
                    Read();
                }
                catch (InvalidOperationException ex) { status.Text = ex.Message; }
                catch (Exception) { status.Text = "Não foi possível salvar as configurações."; }
            };
            form.ShowDialog();
        }
    }

    static TextBox Field(TableLayoutPanel layout, string label, string value)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true });
        var field = new TextBox { Text = value, Dock = DockStyle.Top };
        layout.Controls.Add(field);
        return field;
    }

    protected override void ExitThreadCore()
    {
        closed = true;
        position.Dispose();
        refresh.Dispose();
        hint.Dispose();
        tray.ContextMenuStrip.Dispose();
        tray.Dispose();
        widget.Dispose();
        base.ExitThreadCore();
    }

    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test")
        {
            var watts = Solar.Watts(new Sensor { state = "1.2", attributes = new Attributes { unit_of_measurement = "kW" } })
                + Solar.Watts(new Sensor { state = "248", attributes = new Attributes { unit_of_measurement = "W" } });
            if (watts != 1448 || Solar.Format(watts) != "1.448 W  ·  32,9%" ||
                Solar.Format(4400) != "4.400 W  ·  100,0%") return 1;
            try { Solar.Watts(new Sensor { state = "unavailable", attributes = new Attributes { unit_of_measurement = "W" } }); }
            catch (InvalidOperationException) { return 0; }
            return 1;
        }
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool created;
        using (var mutex = new Mutex(true, @"Local\MonitorSolar", out created))
        {
            if (created) using (var monitor = new Monitor()) Application.Run(monitor);
        }
        return 0;
    }
}
