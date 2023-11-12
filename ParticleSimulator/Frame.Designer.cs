namespace ParticleSimulator
{
    partial class Frame
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            PicBox = new PictureBox();
            TestButton = new Button();
            ((System.ComponentModel.ISupportInitialize)PicBox).BeginInit();
            SuspendLayout();
            // 
            // PicBox
            // 
            PicBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            PicBox.BackColor = Color.DimGray;
            PicBox.Location = new Point(0, 0);
            PicBox.Name = "PicBox";
            PicBox.Size = new Size(560, 450);
            PicBox.TabIndex = 0;
            PicBox.TabStop = false;
            // 
            // TestButton
            // 
            TestButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            TestButton.Location = new Point(605, 8);
            TestButton.Name = "TestButton";
            TestButton.Size = new Size(183, 23);
            TestButton.TabIndex = 1;
            TestButton.Text = "button1";
            TestButton.UseVisualStyleBackColor = true;
            TestButton.Click += TestButton_Click;
            // 
            // Frame
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(TestButton);
            Controls.Add(PicBox);
            Name = "Frame";
            Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)PicBox).EndInit();
            ResumeLayout(false);
        }

        #endregion

        public PictureBox PicBox;
        private Button TestButton;
    }
}