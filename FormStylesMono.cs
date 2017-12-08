using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace com.clusterrr.hakchi_gui
{
    public class FormStylesMono : Form
    {
        static Dictionary<Form, Size> formSizes = new Dictionary<Form, Size>();

        public static void AdjustStyles(object sender, EventArgs e = null)
        {
            Font niceFont = new Font("Roboto Regular", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Form form = (Form) sender;

            //form.SuspendLayout();
            //form.ResumeLayout(true);
            //form.AutoScaleMode = AutoScaleMode.Font;
            //form.PerformAutoScale();
            //form.Font = new Font(FontFamily.Families, 9F);

            form.Font = niceFont;

            foreach (Control childControl in form.Controls)
            {
                childControl.Font = niceFont;
            }

            if (formSizes.ContainsKey(form)) form.Size = formSizes[form];
            formSizes[form] = form.Size;

            form.Invalidate();
        }
    }
}
