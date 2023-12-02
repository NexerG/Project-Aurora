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
            TB_SmoothingRadius = new TextBox();
            Smoothing_radius = new Label();
            Target_Density = new Label();
            TB_TargetDensity = new TextBox();
            Pressure_mult = new Label();
            TB_PressureMult = new TextBox();
            Viscosity_str = new Label();
            TB_ViscosityStrength = new TextBox();
            button1 = new Button();
            label1 = new Label();
            TB_GravStr = new TextBox();
            ((System.ComponentModel.ISupportInitialize)PicBox).BeginInit();
            SuspendLayout();
            // 
            // PicBox
            // 
            PicBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            PicBox.BackColor = Color.DimGray;
            PicBox.Location = new Point(0, 0);
            PicBox.Name = "PicBox";
            PicBox.Size = new Size(904, 627);
            PicBox.TabIndex = 0;
            PicBox.TabStop = false;
            // 
            // TestButton
            // 
            TestButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            TestButton.Location = new Point(924, 8);
            TestButton.Name = "TestButton";
            TestButton.Size = new Size(208, 23);
            TestButton.TabIndex = 1;
            TestButton.Text = "Reset Sim";
            TestButton.UseVisualStyleBackColor = true;
            TestButton.Click += TestButton_Click;
            // 
            // TB_SmoothingRadius
            // 
            TB_SmoothingRadius.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            TB_SmoothingRadius.Location = new Point(1034, 63);
            TB_SmoothingRadius.Name = "TB_SmoothingRadius";
            TB_SmoothingRadius.Size = new Size(98, 23);
            TB_SmoothingRadius.TabIndex = 2;
            TB_SmoothingRadius.Validated += TB_SmoothingRadius_Validated;
            // 
            // Smoothing_radius
            // 
            Smoothing_radius.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Smoothing_radius.AutoSize = true;
            Smoothing_radius.Location = new Point(924, 66);
            Smoothing_radius.Name = "Smoothing_radius";
            Smoothing_radius.Size = new Size(104, 15);
            Smoothing_radius.TabIndex = 3;
            Smoothing_radius.Text = "Smoothing Radius";
            // 
            // Target_Density
            // 
            Target_Density.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Target_Density.AutoSize = true;
            Target_Density.Location = new Point(924, 95);
            Target_Density.Name = "Target_Density";
            Target_Density.Size = new Size(81, 15);
            Target_Density.TabIndex = 5;
            Target_Density.Text = "Target Density";
            // 
            // TB_TargetDensity
            // 
            TB_TargetDensity.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            TB_TargetDensity.Location = new Point(1034, 92);
            TB_TargetDensity.Name = "TB_TargetDensity";
            TB_TargetDensity.Size = new Size(98, 23);
            TB_TargetDensity.TabIndex = 4;
            TB_TargetDensity.Validated += TB_TargetDensity_Validated;
            // 
            // Pressure_mult
            // 
            Pressure_mult.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Pressure_mult.AutoSize = true;
            Pressure_mult.Location = new Point(924, 124);
            Pressure_mult.Name = "Pressure_mult";
            Pressure_mult.Size = new Size(105, 15);
            Pressure_mult.TabIndex = 7;
            Pressure_mult.Text = "Pressure Multiplier";
            // 
            // TB_PressureMult
            // 
            TB_PressureMult.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            TB_PressureMult.Location = new Point(1034, 121);
            TB_PressureMult.Name = "TB_PressureMult";
            TB_PressureMult.Size = new Size(98, 23);
            TB_PressureMult.TabIndex = 6;
            TB_PressureMult.Validated += TB_PressureMult_Validated;
            // 
            // Viscosity_str
            // 
            Viscosity_str.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Viscosity_str.AutoSize = true;
            Viscosity_str.Location = new Point(924, 153);
            Viscosity_str.Name = "Viscosity_str";
            Viscosity_str.Size = new Size(101, 15);
            Viscosity_str.TabIndex = 9;
            Viscosity_str.Text = "Viscosity Strength";
            // 
            // TB_ViscosityStrength
            // 
            TB_ViscosityStrength.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            TB_ViscosityStrength.Location = new Point(1034, 150);
            TB_ViscosityStrength.Name = "TB_ViscosityStrength";
            TB_ViscosityStrength.Size = new Size(98, 23);
            TB_ViscosityStrength.TabIndex = 8;
            TB_ViscosityStrength.Validated += TB_ViscosityStrength_Validated;
            // 
            // button1
            // 
            button1.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button1.DialogResult = DialogResult.OK;
            button1.Location = new Point(924, 34);
            button1.Name = "button1";
            button1.Size = new Size(208, 23);
            button1.TabIndex = 10;
            button1.Text = "Commit";
            button1.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            label1.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            label1.AutoSize = true;
            label1.Location = new Point(924, 182);
            label1.Name = "label1";
            label1.Size = new Size(92, 15);
            label1.TabIndex = 12;
            label1.Text = "Gravity Strength";
            // 
            // TB_GravStr
            // 
            TB_GravStr.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            TB_GravStr.Location = new Point(1034, 179);
            TB_GravStr.Name = "TB_GravStr";
            TB_GravStr.Size = new Size(98, 23);
            TB_GravStr.TabIndex = 11;
            TB_GravStr.Validated += TB_GravStr_Validated;
            // 
            // Frame
            // 
            AcceptButton = button1;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1144, 627);
            Controls.Add(label1);
            Controls.Add(TB_GravStr);
            Controls.Add(button1);
            Controls.Add(Viscosity_str);
            Controls.Add(TB_ViscosityStrength);
            Controls.Add(Pressure_mult);
            Controls.Add(TB_PressureMult);
            Controls.Add(Target_Density);
            Controls.Add(TB_TargetDensity);
            Controls.Add(Smoothing_radius);
            Controls.Add(TB_SmoothingRadius);
            Controls.Add(TestButton);
            Controls.Add(PicBox);
            Name = "Frame";
            ((System.ComponentModel.ISupportInitialize)PicBox).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        public PictureBox PicBox;
        private Button TestButton;

        public TextBox TB_SmoothingRadius;
        private Label Smoothing_radius;

        private Label Target_Density;
        public TextBox TB_TargetDensity;

        private Label Pressure_mult;
        public TextBox TB_PressureMult;

        private Label Viscosity_str;
        public TextBox TB_ViscosityStrength;

        private Button button1;

        private Label label1;
        public TextBox TB_GravStr;
    }
}