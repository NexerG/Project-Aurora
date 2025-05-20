using ArctisAurora.CustomEntities;
using ArctisAurora.EngineWork;
using ArctisAurora.EngineWork.Renderer;
using ArctisAurora.EngineWork.Renderer.Helpers;
using ArctisAurora.EngineWork.Renderer.RendererTypes;

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
            isDrawingLight.Location = new Point(5, 75);
            isDrawingLight.Name = "isDrawingLight";
            isDrawingLight.Size = new Size(102, 19);
            isDrawingLight.TabIndex = 3;
            isDrawingLight.Text = "DrawingLight?";
            isDrawingLight.UseVisualStyleBackColor = true;
            isDrawingLight.CheckedChanged += isDrawingLight_CheckedChanged;
            // 
            // ColorPickerPB
            // 
            ColorPickerPB.Image = (Image)resources.GetObject("ColorPickerPB.Image");
            ColorPickerPB.Location = new Point(5, 115);
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
            label1.Location = new Point(40, 275);
            label1.Name = "label1";
            label1.Size = new Size(79, 15);
            label1.TabIndex = 5;
            label1.Text = "Chosen Color";
            // 
            // ColorPreviewLabel
            // 
            ColorPreviewLabel.BackColor = Color.White;
            ColorPreviewLabel.BorderStyle = BorderStyle.FixedSingle;
            ColorPreviewLabel.Location = new Point(130, 274);
            ColorPreviewLabel.Name = "ColorPreviewLabel";
            ColorPreviewLabel.Size = new Size(53, 20);
            ColorPreviewLabel.TabIndex = 6;
            // 
            // Frame
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(239, 324);
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

            RadianceCascades2D.worldData.brushColor = new Silk.NET.Maths.Vector4D<float>(r, g, b, 1);
            AVulkanBufferHandler.UpdateBuffer(ref RadianceCascades2D.worldData, ref RadianceCascades2D.mousePosBuffer, ref RadianceCascades2D.mousePosMemory, Silk.NET.Vulkan.BufferUsageFlags.UniformBufferBit);
        }

        private void isDrawingLight_CheckedChanged(object sender, EventArgs e)
        {
            // flip drawing boolean
        }

        private void LayerIndex_ValueChanged(object sender, EventArgs e)
        {
            RadianceCascades2D.worldData.editableLayer = (int)LayerIndex.Value;
            AVulkanBufferHandler.UpdateBuffer(ref RadianceCascades2D.worldData, ref RadianceCascades2D.mousePosBuffer, ref RadianceCascades2D.mousePosMemory, Silk.NET.Vulkan.BufferUsageFlags.UniformBufferBit);
        }
    }
}