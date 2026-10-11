using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using DarkUI.Forms;
using Grimoire.Tools.Wiki;

namespace Grimoire.UI
{
    public class WikiForm : DarkForm
    {
        private ElementHost _elementHost;
        private WikiView _wikiView;

        public static WikiForm Instance { get; } = new WikiForm();

        private WikiForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            _elementHost = new ElementHost
            {
                Dock = DockStyle.Fill
            };

            _wikiView = new WikiView();
            _elementHost.Child = _wikiView;

            SuspendLayout();

            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(900, 600);
            MinimumSize = new System.Drawing.Size(600, 400);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            Name = "WikiForm";
            Text = "Wiki Browser";

            Controls.Add(_elementHost);

            FormClosing += WikiForm_FormClosing;

            ResumeLayout(false);
        }

        private void WikiForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_elementHost != null)
                {
                    _elementHost.Child = null;
                    _elementHost.Dispose();
                    _elementHost = null;
                }
                _wikiView = null;
            }
            base.Dispose(disposing);
        }
    }
}
