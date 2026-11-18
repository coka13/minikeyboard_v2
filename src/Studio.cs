using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

// The vendor assembly remains the device controller. Its controls are reparented,
// rather than replaced, so their original HID handlers and validation still run.
internal static class Studio
{
    static readonly Color Background = Color.FromArgb(17, 19, 25);
    static readonly Color Surface = Color.FromArgb(25, 28, 36);
    static readonly Color KeySurface = Color.FromArgb(37, 41, 53);
    static readonly Color Border = Color.FromArgb(49, 54, 68);
    static readonly Color Ink = Color.FromArgb(235, 237, 246);
    static readonly Color Muted = Color.FromArgb(146, 154, 175);
    static readonly Color Accent = Color.FromArgb(180, 164, 255);
    static Form form;
    static Panel root;
    static Label selection;
    static Button selected;
    static readonly List<Button> targets = new List<Button>();
    static readonly List<Button> categories = new List<Button>();
    static readonly HashSet<Control> wiredPages = new HashSet<Control>();
    static readonly ToolTip tips = new ToolTip();
    static Button activeCategory;
    static bool pending;
    static Label saveState;
    static Label assignmentHint;
    static LiveInspector inspector;
    static Button inspectButton;
    static Label liveOutput;
    static Label liveHint;

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
        try
        {
            Assembly vendor = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MINI KeyBoard.exe"));
            form = (Form)Activator.CreateInstance(vendor.GetType("HIDTester.FormMain", true));
            form.Shown += delegate { form.BeginInvoke((MethodInvoker)Build); };
            Application.Run(form);
            // The vendor starts a foreground USB polling thread; stop it when
            // the user closes the studio, rather than leaving a hidden process.
            Environment.Exit(0);
        }
        catch (Exception error)
        {
            MessageBox.Show("Mini Keyboard Studio could not start. Keep the original MINI KeyBoard.exe and its libraries beside this launcher.\n\n" + error.Message,
                "Mini Keyboard Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static Control Original(string name)
    {
        FieldInfo field = form.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null) throw new InvalidOperationException("The vendor control was not found: " + name);
        return (Control)field.GetValue(form);
    }

    static Label Text(Control parent, string value, int x, int y, int width, int height, float size, Color color, bool bold)
    {
        Label label = new Label { Text = value, Bounds = new Rectangle(x, y, width, height),
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = color, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
        parent.Controls.Add(label);
        return label;
    }

    static void Move(Control control, Control parent, int x, int y, int width, int height)
    {
        control.Dock = DockStyle.None;
        control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        parent.Controls.Add(control);
        control.Bounds = new Rectangle(x, y, width, height);
        control.Visible = true;
        control.Font = new Font("Segoe UI", 10F);
        control.ForeColor = Ink;
    }

    static void ButtonStyle(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(54, 59, 77);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(72, 63, 103);
        button.UseVisualStyleBackColor = false;
        button.BackColor = KeySurface;
        button.ForeColor = Ink;
        button.Font = new Font("Segoe UI", 9F);
        button.Cursor = Cursors.Hand;
    }

    static void Theme(Control parent)
    {
        ContainerControl container = parent as ContainerControl;
        if (container != null) container.AutoScaleMode = AutoScaleMode.None;
        parent.BackColor = Surface;
        parent.ForeColor = Ink;
        parent.Font = new Font("Segoe UI", 9F);
        foreach (Control child in parent.Controls)
        {
            Button button = child as Button;
            if (button != null) ButtonStyle(button);
            else if (child is TextBox) { child.BackColor = KeySurface; child.ForeColor = Ink; }
            else Theme(child);
        }
    }

    static void Dirty()
    {
        pending = true;
        saveState.Text = "Changes ready to send";
        saveState.ForeColor = Accent;
    }

    static void Build()
    {
        form.SuspendLayout();
        form.Text = "Mini Keyboard Studio";
        form.AutoScaleMode = AutoScaleMode.None;
        form.BackColor = Background;
        form.Font = new Font("Segoe UI", 10F);
        // Remove the vendor's proportional resizing callback. The studio uses a
        // fixed logical surface with scrolling on small displays instead.
        EventInfo resize = typeof(Control).GetEvent("SizeChanged");
        MethodInfo handler = form.GetType().GetMethod("MainPage_SizeChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        if (handler != null) resize.RemoveEventHandler(form, Delegate.CreateDelegate(typeof(EventHandler), form, handler));
        form.ClientSize = new Size(1180, 885);
        form.MinimumSize = new Size(840, 640);
        form.StartPosition = FormStartPosition.CenterScreen;
        Rectangle work = Screen.FromControl(form).WorkingArea;
        form.Size = new Size(Math.Min(form.Width, work.Width - 32), Math.Min(form.Height, work.Height - 32));
        form.Location = new Point(work.Left + (work.Width - form.Width) / 2, work.Top + (work.Height - form.Height) / 2);

        // Existing controls not used by the studio remain alive for vendor code.
        foreach (Control control in form.Controls) control.Hide();
        Panel viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Background };
        root = new Panel { Size = new Size(1180, 1015), BackColor = Background };
        viewport.Controls.Add(root);
        form.Controls.Add(viewport);
        viewport.BringToFront();

        // A centered, painted mark has no font-bearing clipping or overlap with
        // its neighbour at Windows display scaling factors.
        Logo logo = new Logo { Bounds = new Rectangle(24, 21, 58, 58) };
        root.Controls.Add(logo);
        Label heading = Text(root, "Mini Keyboard", 104, 16, 540, 45, 23, Ink, true);
        heading.AutoSize = true;
        Text(root, "S T U D I O   /   DEVICE CONFIGURATOR", 106, 60, 550, 24, 8, Muted, false);
        Label status = (Label)Original("stateLabel");
        Label statusBadge = Text(root, "Checking device…", 914, 29, 242, 34, 10, Muted, false);
        statusBadge.TextAlign = ContentAlignment.MiddleCenter;
        statusBadge.BackColor = KeySurface;
        Action refreshStatus = delegate {
            statusBadge.Text = "●  " + status.Text;
            statusBadge.ForeColor = status.Text.Equals("Connected", StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb(139, 217, 179) : Muted;
        };
        status.TextChanged += delegate { refreshStatus(); };
        refreshStatus();
        tips.SetToolTip(statusBadge, "Connect your macro keyboard by USB. Device status comes from the original controller.");

        Card device = new Card { Bounds = new Rectangle(24, 102, 708, 308) };
        Card editor = new Card { Bounds = new Rectangle(748, 102, 408, 308) };
        root.Controls.Add(device);
        root.Controls.Add(editor);
        Text(device, "01  /  YOUR CONTROLS", 22, 15, 300, 28, 10, Muted, true);
        Text(device, "Select a key or dial action", 22, 43, 365, 29, 17, Ink, true);
        Text(device, "KEYPAD", 24, 81, 180, 20, 8, Muted, true);
        Text(device, "ROTARY DIALS", 366, 81, 240, 20, 8, Muted, true);

        for (int i = 1; i <= 16; i++)
        {
            Button key = (Button)Original("KEY" + i);
            if (!HasVendorClick(key)) continue;
            Move(key, device, 24 + ((i - 1) % 4) * 76, 107 + ((i - 1) / 4) * 54, 66, 46);
            key.Text = i.ToString("00");
            key.AccessibleName = "Key " + i;
            RegisterTarget(key, "Key " + i.ToString("00"));
        }

        string[] action = { "Left", "Centre", "Right" };
        string[] captions = { "↶", "Press", "↷" };
        for (int dial = 1; dial <= 3; dial++)
        {
            if (!HasVendorClick((Button)Original("K" + dial + "_Left"))) continue;
            Text(device, "0" + dial, 366, 128 + (dial - 1) * 78, 32, 34, 10, Muted, true);
            for (int j = 0; j < 3; j++)
            {
                Button knob = (Button)Original("K" + dial + "_" + action[j]);
                Move(knob, device, 405 + j * 84, 121 + (dial - 1) * 78, 74, 48);
                knob.Text = captions[j];
                string label = "Dial " + dial + " · " + (j == 0 ? "Turn left" : j == 1 ? "Press" : "Turn right");
                knob.AccessibleName = label;
                tips.SetToolTip(knob, label);
                RegisterTarget(knob, label);
            }
        }
        Text(device, "Keys follow the device order: left to right, top to bottom.", 24, 275, 660, 22, 9, Muted, false);

        Control layers = Original("flowLayoutPanel_LayerFun");
        Move(layers, device, 447, 25, 240, 32);
        Theme(layers);
        foreach (Control layer in layers.Controls)
        {
            layer.Size = new Size(236, 28);
            foreach (Control radio in layer.Controls)
            {
                RadioButton rb = radio as RadioButton;
                if (rb == null) continue;
                rb.Appearance = Appearance.Button;
                rb.FlatStyle = FlatStyle.Flat;
                rb.FlatAppearance.BorderSize = 0;
                rb.FlatAppearance.CheckedBackColor = Color.FromArgb(66, 55, 97);
                rb.TextAlign = ContentAlignment.MiddleCenter;
                rb.Size = new Size(72, 26);
                rb.Location = new Point(rb.Name.EndsWith("3") ? 164 : rb.Name.EndsWith("2") ? 82 : 0, 0);
                rb.Font = new Font("Segoe UI", 9F);
                rb.Cursor = Cursors.Hand;
            }
        }

        Text(editor, "02  /  ASSIGNMENT", 22, 15, 360, 28, 10, Muted, true);
        selection = Text(editor, "Choose a control", 22, 47, 360, 34, 19, Ink, true);
        Text(editor, "MODIFIERS / FUNCTION", 22, 96, 360, 22, 8, Muted, true);
        Control function = Original("SetFunText");
        Move(function, editor, 24, 126, 358, 32);
        StyleInput(function);
        Text(editor, "KEY / ACTION SEQUENCE", 22, 174, 360, 22, 8, Muted, true);
        Control sequence = Original("SetText");
        Move(sequence, editor, 24, 204, 358, 32);
        StyleInput(sequence);
        assignmentHint = Text(editor, "Device assignments cannot be read automatically.", 22, 253, 360, 45, 9, Muted, false);
        tips.SetToolTip(assignmentHint, "Inspect live output to see what the physical keyboard actually sends. No history is saved.");
        assignmentHint.Visible = false;
        Button inspectShortcut = new Button { Text = "Inspect this control's live output", Bounds = new Rectangle(24, 254, 358, 34) };
        ButtonStyle(inspectShortcut);
        editor.Controls.Add(inspectShortcut);

        Text(root, "03  /  ACTION LIBRARY", 28, 428, 330, 29, 10, Muted, true);
        Text(root, "Click an action to add it to the selected control.", 658, 428, 498, 29, 10, Muted, false);
        Card library = new Card { Bounds = new Rectangle(24, 468, 1132, 350) };
        root.Controls.Add(library);
        FlowLayoutPanel menu = (FlowLayoutPanel)Original("flowLayoutPanel1");
        Move(menu, library, 12, 10, 150, 330);
        menu.FlowDirection = FlowDirection.TopDown;
        menu.WrapContents = false;
        menu.BackColor = Surface;
        string[] names = { "Keyboard", "Modifiers", "Media", "Lighting", "Mouse", "More" };
        int categoryIndex = 0;
        foreach (Control control in menu.Controls)
        {
            Button button = control as Button;
            if (button == null) continue;
            if (String.IsNullOrWhiteSpace(button.Text)) { button.Hide(); continue; }
            // Vendor handlers use button.Text as a dictionary key. Keep that text
            // intact and paint the friendly name separately.
            string title = names[Math.Min(categoryIndex++, names.Length - 1)];
            button.Size = new Size(138, 44);
            button.Margin = new Padding(0, 0, 0, 8);
            ButtonStyle(button);
            button.AccessibleName = title;
            categories.Add(button);
            PaintCategory(button, title);
            button.MouseDown += delegate { activeCategory = button; foreach (Button item in categories) item.Invalidate(); };
            button.Click += delegate { activeCategory = button; foreach (Button item in categories) item.Invalidate(); };
        }
        if (categories.Count > 0) activeCategory = categories[0];
        SplitContainer split = (SplitContainer)Original("splitContainer1");
        Panel actions = split.Panel2;
        // Reuse the actual Panel2: the controller still inserts action pages here.
        // A SplitterPanel cannot be reparented, so keep its SplitContainer and
        // collapse the now-redundant first panel.
        Move(split, library, 177, 0, 940, 346);
        split.Panel1Collapsed = true;
        split.IsSplitterFixed = true;
        split.BorderStyle = BorderStyle.None;
        split.BackColor = Surface;
        actions.AutoScroll = true;
        Theme(actions);
        actions.ControlAdded += delegate(object sender, ControlEventArgs e) { WireActionPage(e.Control); };
        foreach (Control page in actions.Controls) WireActionPage(page);

        saveState = Text(root, "Select a control to get started", 28, 834, 560, 29, 10, Muted, false);
        Label feedback = (Label)Original("label_Dowload_Dsp");
        Move(feedback, root, 28, 861, 760, 22);
        feedback.Font = new Font("Segoe UI", 9F);
        feedback.ForeColor = Accent;
        feedback.BackColor = Background;
        feedback.Hide();
        Button clear = (Button)Original("Key_Clear");
        Move(clear, root, 776, 831, 162, 42);
        ButtonStyle(clear);
        clear.Text = "Clear assignment";
        clear.Click += delegate { Dirty(); };
        Button download = (Button)Original("Download");
        Move(download, root, 950, 831, 206, 42);
        ButtonStyle(download);
        download.Text = "Send to keyboard   →";
        download.BackColor = Accent;
        download.ForeColor = Background;
        download.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        tips.SetToolTip(download, "Write the current configuration to your connected keyboard.");
        download.Click += delegate { if (pending) saveState.Text = "Check the device feedback for transfer status"; };
        Card live = new Card { Bounds = new Rectangle(24, 897, 1132, 100) };
        root.Controls.Add(live);
        Text(live, "LIVE DEVICE OUTPUT", 18, 7, 245, 24, 9, Muted, true);
        liveOutput = Text(live, "No output inspected yet", 18, 31, 840, 29, 14, Ink, true);
        liveOutput.AutoEllipsis = true;
        liveHint = Text(live, "Select a control, then inspect and press it on the physical keyboard. Shortcuts still run in Windows.", 18, 65, 880, 25, 9, Muted, false);
        inspectButton = new Button { Text = "Inspect live output", Bounds = new Rectangle(909, 29, 202, 43) };
        ButtonStyle(inspectButton);
        live.Controls.Add(inspectButton);
        inspector = new LiveInspector(form);
        inspector.Changed += delegate(string output, string detail, bool listening) {
            liveOutput.Text = output;
            tips.SetToolTip(liveOutput, output);
            liveHint.Text = detail;
            inspectButton.Text = listening ? "Stop inspection" : "Inspect live output";
        };
        inspectButton.Click += delegate {
            if (inspector.Listening) inspector.Cancel();
            else if (selected != null) inspector.Start(selection.Text);
        };
        inspectShortcut.Click += delegate { viewport.ScrollControlIntoView(live); inspectButton.PerformClick(); };
        form.Deactivate += delegate { inspector.Cancel(); };
        form.FormClosed += delegate { inspector.Dispose(); };
        using (Graphics graphics = form.CreateGraphics())
        {
            float scale = graphics.DpiX / 96F;
            if (Math.Abs(scale - 1F) > .01F) root.Scale(new SizeF(scale, scale));
        }
        form.ClientSize = new Size(Math.Min(root.Width + 20, work.Width - 32), Math.Min(root.Height + 20, work.Height - 72));
        form.Location = new Point(work.Left + (work.Width - form.Width) / 2, work.Top + (work.Height - form.Height) / 2);
        form.ResumeLayout(true);
        ((Button)Original("KEY1")).PerformClick();
        pending = false;
        saveState.Text = "Choose an action for the selected control";
        saveState.ForeColor = Muted;
    }

    static void StyleInput(Control control)
    {
        control.BackColor = KeySurface;
        control.ForeColor = Ink;
        control.Font = new Font("Segoe UI", 13F);
        ((TextBox)control).BorderStyle = BorderStyle.FixedSingle;
        // The controller builds HID bytes from library clicks, not typed text.
        ((TextBox)control).ReadOnly = true;
    }

    static void RegisterTarget(Button button, string title)
    {
        ButtonStyle(button);
        targets.Add(button);
        button.Click += delegate
        {
            selected = button;
            selection.Text = title;
            foreach (Button target in targets) target.Invalidate();
            if (inspector != null) inspector.Cancel();
        };
        button.Paint += delegate(object sender, PaintEventArgs e)
        {
            bool on = selected == button;
            using (SolidBrush fill = new SolidBrush(on ? Color.FromArgb(65, 54, 96) : KeySurface)) e.Graphics.FillRectangle(fill, button.ClientRectangle);
            using (Pen line = new Pen(on ? Accent : Border, on ? 2 : 1)) e.Graphics.DrawRectangle(line, 1, 1, button.Width - 3, button.Height - 3);
            TextRenderer.DrawText(e.Graphics, button.Text, button.Font, button.ClientRectangle, on ? Color.White : Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (button.Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(button.ClientRectangle, -5, -5), Accent, KeySurface);
        };
    }

    static void PaintCategory(Button button, string title)
    {
        button.Paint += delegate(object sender, PaintEventArgs e)
        {
            bool on = activeCategory == button;
            using (SolidBrush fill = new SolidBrush(on ? Color.FromArgb(49, 43, 69) : Surface)) e.Graphics.FillRectangle(fill, button.ClientRectangle);
            if (on) using (SolidBrush mark = new SolidBrush(Accent)) e.Graphics.FillRectangle(mark, 0, 9, 3, button.Height - 18);
            TextRenderer.DrawText(e.Graphics, title, button.Font, new Rectangle(14, 0, button.Width - 20, button.Height), on ? Accent : Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (button.Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(button.ClientRectangle, -5, -5));
        };
    }

    static void WireActionPage(Control page)
    {
        Theme(page);
        if (!wiredPages.Add(page)) return;
        // Vendor pages use font-based autoscaling with a different design font.
        // Fit their actual content into the library once, without stretching text.
        int right = 1, bottom = 1;
        foreach (Control child in page.Controls)
        { right = Math.Max(right, child.Right); bottom = Math.Max(bottom, child.Bottom); }
        float fit = Math.Min(1F, Math.Min(914F / right, 334F / bottom));
        foreach (Control child in page.Controls)
            child.Bounds = new Rectangle((int)(child.Left * fit), (int)(child.Top * fit),
                Math.Max(1, (int)(child.Width * fit)), Math.Max(1, (int)(child.Height * fit)));
        page.Dock = DockStyle.None;
        page.Size = new Size(914, 346);
        foreach (Control control in page.Controls)
        {
            Button action = control as Button;
            if (action != null) action.Click += delegate { Dirty(); };
            else if (control.HasChildren) WireActionPage(control);
        }
    }

    static bool HasVendorClick(Button button)
    {
        PropertyInfo events = typeof(System.ComponentModel.Component).GetProperty("Events", BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo click = typeof(Control).GetField("EventClick", BindingFlags.NonPublic | BindingFlags.Static);
        if (events == null || click == null) throw new InvalidOperationException("Unsupported Windows Forms runtime.");
        System.ComponentModel.EventHandlerList handlers = (System.ComponentModel.EventHandlerList)events.GetValue(button, null);
        return handlers[click.GetValue(null)] != null;
    }

    sealed class Logo : Control
    {
        public Logo() { DoubleBuffered = true; BackColor = Color.FromArgb(49, 43, 69); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Font font = new Font("Segoe UI", 15F, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, "MK", font, Rectangle.Inflate(ClientRectangle, -6, -6), Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
    }

    sealed class Card : Panel
    {
        public Card() { DoubleBuffered = true; BackColor = Surface; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = new GraphicsPath())
            {
                int d = 18, w = Width - 1, h = Height - 1;
                path.AddArc(0, 0, d, d, 180, 90); path.AddArc(w - d, 0, d, d, 270, 90);
                path.AddArc(w - d, h - d, d, d, 0, 90); path.AddArc(0, h - d, d, d, 90, 90); path.CloseFigure();
                using (Pen pen = new Pen(Border)) e.Graphics.DrawPath(pen, path);
            }
        }
    }
}
