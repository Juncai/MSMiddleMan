using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuaternionTrans
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.textBox_sin.Text = Math.Sin(double.Parse(this.textBox_ang.Text)).ToString();



            double rotZ = Double.Parse(this.textBox_rotZ.Text);
            double rotY = Double.Parse(this.textBox_rotY.Text);
            double rotX = Double.Parse(this.textBox_rotX.Text);

            double cos_z_2 = Math.Cos(0.5 * rotZ);
            double cos_y_2 = Math.Cos(0.5 * rotY);
            double cos_x_2 = Math.Cos(0.5 * rotX);

            double sin_z_2 = Math.Sin(0.5 * rotZ);
            double sin_y_2 = Math.Sin(0.5 * rotY);
            double sin_x_2 = Math.Sin(0.5 * rotX);

            // and now compute quaternion
            double quatW = cos_z_2 * cos_y_2 * cos_x_2 + sin_z_2 * sin_y_2 * sin_x_2;
            double quatX = cos_z_2 * cos_y_2 * sin_x_2 - sin_z_2 * sin_y_2 * cos_x_2;
            double quatY = cos_z_2 * sin_y_2 * cos_x_2 + sin_z_2 * cos_y_2 * sin_x_2;
            double quatZ = sin_z_2 * cos_y_2 * cos_x_2 - cos_z_2 * sin_y_2 * sin_x_2;

            string mess = string.Format("sbm receiver skeleton User1 generic rotation skullbase {0} {1} {2} {3}", quatW, quatX, quatY, quatZ);
            this.textBox1.Text = mess;
            return;
        }
    }
}
