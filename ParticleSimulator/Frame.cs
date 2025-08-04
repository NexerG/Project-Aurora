using System.Runtime.ConstrainedExecution;
using ArctisAurora.CustomEntities;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Rendering;
using ArctisAurora.EngineWork.Rendering.Helpers;
using ArctisAurora.EngineWork.Rendering.RendererTypes;
using Silk.NET.Vulkan;

namespace ArctisAurora
{
    public partial class Frame : Form
    {
        internal Engine engine = null;
        bool is3D = true;
        int parts = 0;

        public Frame()
        {
            //initialization
            //InitializeComponentBehaviour();
            InitializeComponent();
        }

        private void Frame_Load(object sender, EventArgs e)
        {
            //GLControl.Paint += GLControl_Paint;
            //GLControl.Resize += GLControl_Resize;
            engine = new Engine();
            engine.Init(this);
        }

        private void Frame_FormClosing(object sender, FormClosingEventArgs e)
        {

        }

        private void Frame_KeyDown(object sender, KeyEventArgs e)
        {
            //engine.KeyboardHandler(e);
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Frame));
            AddLayer = new Button();
            SelectedLayerLabel = new Label();
            LayerIndex = new NumericUpDown();
            isDrawingLight = new CheckBox();
            colorDialog1 = new ColorDialog();
            ColorPickerPB = new PictureBox();
            label1 = new Label();
            ColorPreviewLabel = new Label();
            BrushSizeBar = new HScrollBar();
            BrushSizeLabel = new Label();
            EmissiveBar = new HScrollBar();
            label2 = new Label();
            label3 = new Label();
            EmissiveLabel = new Label();
            LightStrLabel = new Label();
            label5 = new Label();
            LightStrBar = new HScrollBar();
            IsPhosphorous = new CheckBox();
            this.Color_reset = new Button();
            ((System.ComponentModel.ISupportInitialize)LayerIndex).BeginInit();
            ((System.ComponentModel.ISupportInitialize)ColorPickerPB).BeginInit();
            SuspendLayout();
            // 
            // AddLayer
            // 
            AddLayer.Location = new Point(3, 6);
            AddLayer.Name = "AddLayer";
            AddLayer.Size = new Size(228, 23);
            AddLayer.TabIndex = 0;
            AddLayer.Text = "Add Layer";
            AddLayer.UseVisualStyleBackColor = true;
            AddLayer.Click += AddLayer_Click;
            // 
            // SelectedLayerLabel
            // 
            SelectedLayerLabel.AutoSize = true;
            SelectedLayerLabel.Location = new Point(5, 41);
            SelectedLayerLabel.Name = "SelectedLayerLabel";
            SelectedLayerLabel.Size = new Size(82, 15);
            SelectedLayerLabel.TabIndex = 1;
            SelectedLayerLabel.Text = "Selected Layer";
            // 
            // LayerIndex
            // 
            LayerIndex.Location = new Point(111, 39);
            LayerIndex.Maximum = new decimal(new int[] { 0, 0, 0, 0 });
            LayerIndex.Name = "LayerIndex";
            LayerIndex.Size = new Size(120, 23);
            LayerIndex.TabIndex = 2;
            LayerIndex.ValueChanged += LayerIndex_ValueChanged;
            // 
            // isDrawingLight
            // 
            isDrawingLight.AutoSize = true;
            isDrawingLight.Checked = true;
            isDrawingLight.CheckState = CheckState.Checked;
            isDrawingLight.Location = new Point(5, 100);
            isDrawingLight.Name = "isDrawingLight";
            isDrawingLight.Size = new Size(102, 19);
            isDrawingLight.TabIndex = 3;
            isDrawingLight.Text = "DrawingLight?";
            isDrawingLight.UseVisualStyleBackColor = true;
            isDrawingLight.CheckedChanged += isDrawingLight_CheckedChanged;
            // 
            // ColorPickerPB
            // 
            ColorPickerPB.Image = (System.Drawing.Image)resources.GetObject("ColorPickerPB.Image");
            ColorPickerPB.Location = new Point(1, 219);
            ColorPickerPB.Name = "ColorPickerPB";
            ColorPickerPB.Size = new Size(226, 156);
            ColorPickerPB.SizeMode = PictureBoxSizeMode.AutoSize;
            ColorPickerPB.TabIndex = 4;
            ColorPickerPB.TabStop = false;
            ColorPickerPB.MouseClick += ColorPickerPB_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(9, 379);
            label1.Name = "label1";
            label1.Size = new Size(79, 15);
            label1.TabIndex = 5;
            label1.Text = "Chosen Color";
            // 
            // ColorPreviewLabel
            // 
            ColorPreviewLabel.BackColor = Color.White;
            ColorPreviewLabel.BorderStyle = BorderStyle.FixedSingle;
            ColorPreviewLabel.Location = new Point(99, 378);
            ColorPreviewLabel.Name = "ColorPreviewLabel";
            ColorPreviewLabel.Size = new Size(53, 20);
            ColorPreviewLabel.TabIndex = 6;
            // 
            // BrushSizeBar
            // 
            BrushSizeBar.Location = new Point(6, 429);
            BrushSizeBar.Maximum = 269;
            BrushSizeBar.Minimum = 2;
            BrushSizeBar.Name = "BrushSizeBar";
            BrushSizeBar.Size = new Size(177, 17);
            BrushSizeBar.TabIndex = 7;
            BrushSizeBar.Value = 10;
            BrushSizeBar.ValueChanged += BrushSizeBar_ValueChanged;
            // 
            // BrushSizeLabel
            // 
            BrushSizeLabel.AutoSize = true;
            BrushSizeLabel.Location = new Point(193, 431);
            BrushSizeLabel.Name = "BrushSizeLabel";
            BrushSizeLabel.Size = new Size(13, 15);
            BrushSizeLabel.TabIndex = 8;
            BrushSizeLabel.Text = "5";
            // 
            // EmissiveBar
            // 
            EmissiveBar.Location = new Point(5, 190);
            EmissiveBar.Maximum = 109;
            EmissiveBar.Name = "EmissiveBar";
            EmissiveBar.Size = new Size(177, 17);
            EmissiveBar.TabIndex = 1;
            EmissiveBar.ValueChanged += ColorAlphaBar_ValueChanged;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(6, 405);
            label2.Name = "label2";
            label2.Size = new Size(60, 15);
            label2.TabIndex = 10;
            label2.Text = "Brush Size";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(5, 169);
            label3.Name = "label3";
            label3.Size = new Size(147, 15);
            label3.TabIndex = 11;
            label3.Text = "Color Emissiveness (alpha)";
            // 
            // EmissiveLabel
            // 
            EmissiveLabel.AutoSize = true;
            EmissiveLabel.Location = new Point(193, 192);
            EmissiveLabel.Name = "EmissiveLabel";
            EmissiveLabel.Size = new Size(22, 15);
            EmissiveLabel.TabIndex = 12;
            EmissiveLabel.Text = "0.0";
            // 
            // LightStrLabel
            // 
            LightStrLabel.AutoSize = true;
            LightStrLabel.Location = new Point(194, 145);
            LightStrLabel.Name = "LightStrLabel";
            LightStrLabel.Size = new Size(22, 15);
            LightStrLabel.TabIndex = 15;
            LightStrLabel.Text = "1.0";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(6, 122);
            label5.Name = "label5";
            label5.Size = new Size(82, 15);
            label5.TabIndex = 14;
            label5.Text = "Light Strength";
            // 
            // LightStrBar
            // 
            LightStrBar.Location = new Point(6, 143);
            LightStrBar.Maximum = 109;
            LightStrBar.Name = "LightStrBar";
            LightStrBar.Size = new Size(177, 17);
            LightStrBar.TabIndex = 13;
            LightStrBar.Value = 100;
            LightStrBar.ValueChanged += LightStrBar_ValueChanged;
            // 
            // IsPhosphorous
            // 
            IsPhosphorous.AutoSize = true;
            IsPhosphorous.Location = new Point(5, 75);
            IsPhosphorous.Name = "IsPhosphorous";
            IsPhosphorous.Size = new Size(116, 19);
            IsPhosphorous.TabIndex = 16;
            IsPhosphorous.Text = "IsLayerDecaying?";
            IsPhosphorous.UseVisualStyleBackColor = true;
            IsPhosphorous.CheckedChanged += IsPhosphorous_CheckedChanged;
            // 
            // Color_reset
            // 
            this.Color_reset.Location = new Point(155, 377);
            this.Color_reset.Name = "Color_reset";
            this.Color_reset.Size = new Size(75, 23);
            this.Color_reset.TabIndex = 17;
            this.Color_reset.Text = "ResetColor";
            this.Color_reset.UseVisualStyleBackColor = true;
            this.Color_reset.Click += this.Color_reset_Click;
            // 
            // Frame
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(239, 475);
            Controls.Add(this.Color_reset);
            Controls.Add(IsPhosphorous);
            Controls.Add(LightStrLabel);
            Controls.Add(label5);
            Controls.Add(LightStrBar);
            Controls.Add(EmissiveLabel);
            Controls.Add(label3);
            Controls.Add(label2);
            Controls.Add(EmissiveBar);
            Controls.Add(BrushSizeLabel);
            Controls.Add(BrushSizeBar);
            Controls.Add(ColorPreviewLabel);
            Controls.Add(label1);
            Controls.Add(ColorPickerPB);
            Controls.Add(isDrawingLight);
            Controls.Add(LayerIndex);
            Controls.Add(SelectedLayerLabel);
            Controls.Add(AddLayer);
            Name = "Frame";
            Text = "Aurora";
            FormClosing += Frame_FormClosing;
            Load += Frame_Load;
            KeyDown += Frame_KeyDown;
            ((System.ComponentModel.ISupportInitialize)LayerIndex).EndInit();
            ((System.ComponentModel.ISupportInitialize)ColorPickerPB).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        private void AddLayer_Click(object sender, EventArgs e)
        {
            LayerIndex.Maximum++;
            LayerIndex.Value = LayerIndex.Maximum;
            Layer layer = new Layer();
            VulkanRenderer._rendererInstance.AddEntityToRenderQueue(layer);
        }

        private void ColorPickerPB_Click(object sender, MouseEventArgs e)
        {
            Bitmap colorMap = (Bitmap)ColorPickerPB.Image;
            Color clr = colorMap.GetPixel(e.X, e.Y);
            ColorPreviewLabel.BackColor = clr;

            float r = (float)clr.R / 255;
            float g = (float)clr.G / 255;
            float b = (float)clr.B / 255;

            RadianceCascades2D.worldData.brushColor = new Silk.NET.Maths.Vector3D<float>(r, g, b);
            AVulkanBufferHandler.UpdateBuffer(ref RadianceCascades2D.worldData, ref RadianceCascades2D.mousePosBuffer, ref RadianceCascades2D.mousePosMemory, Silk.NET.Vulkan.BufferUsageFlags.UniformBufferBit);
        }

        private void isDrawingLight_CheckedChanged(object sender, EventArgs e)
        {
            // flip drawing boolean
            RadianceCascades2D.worldData.isEditingLight = isDrawingLight.Checked;
        }

        private void LayerIndex_ValueChanged(object sender, EventArgs e)
        {
            RadianceCascades2D.worldData.editableLayer = (int)LayerIndex.Value;
            AVulkanBufferHandler.UpdateBuffer(ref RadianceCascades2D.worldData, ref RadianceCascades2D.mousePosBuffer, ref RadianceCascades2D.mousePosMemory, Silk.NET.Vulkan.BufferUsageFlags.UniformBufferBit);
        }

        private void BrushSizeBar_ValueChanged(object sender, EventArgs e)
        {
            RadianceCascades2D.worldData.brushSize = BrushSizeBar.Value / 2;
            BrushSizeLabel.Text = BrushSizeBar.Value.ToString();
        }

        private void LightStrBar_ValueChanged(object sender, EventArgs e)
        {
            RadianceCascades2D.worldData.lightStr = (float)LightStrBar.Value / 100;
            LightStrLabel.Text = ((float)LightStrBar.Value / 100).ToString();
            AVulkanBufferHandler.UpdateBuffer(ref RadianceCascades2D.worldData, ref RadianceCascades2D.mousePosBuffer, ref RadianceCascades2D.mousePosMemory, Silk.NET.Vulkan.BufferUsageFlags.UniformBufferBit);
        }

        private void ColorAlphaBar_ValueChanged(object sender, EventArgs e)
        {
            RadianceCascades2D.worldData.emissive = (float)EmissiveBar.Value / 100;
            EmissiveLabel.Text = ((float)EmissiveBar.Value / 100).ToString();
            AVulkanBufferHandler.UpdateBuffer(ref RadianceCascades2D.worldData, ref RadianceCascades2D.mousePosBuffer, ref RadianceCascades2D.mousePosMemory, Silk.NET.Vulkan.BufferUsageFlags.UniformBufferBit);
        }

        private void IsPhosphorous_CheckedChanged(object sender, EventArgs e)
        {
            Layer l = RadianceCascades2D._entitiesToRender[(int)LayerIndex.Value] as Layer;
            l.isPhosphorus = IsPhosphorous.Checked;

            RadianceCascades2D.FreeCommandBuffer();
            RadianceCascades2D._rendererInstance.CreateCommandBuffers();
        }

        private void Color_reset_Click(object sender, EventArgs e)
        {
            ColorPreviewLabel.BackColor = Color.White;
            RadianceCascades2D.worldData.brushColor = new Silk.NET.Maths.Vector3D<float>(1, 1, 1);
            AVulkanBufferHandler.UpdateBuffer(ref RadianceCascades2D.worldData, ref RadianceCascades2D.mousePosBuffer, ref RadianceCascades2D.mousePosMemory, Silk.NET.Vulkan.BufferUsageFlags.UniformBufferBit);
        }
    }
}