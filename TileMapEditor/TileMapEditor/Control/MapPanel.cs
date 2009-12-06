﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using Tiling;

namespace TileMapEditor.Control
{
    public partial class MapPanel : UserControl, DisplayDevice
    {
        #region Private Variables

        private Map m_map;
        private Layer m_selectedLayer;
        private TileSheet m_selectedTileSheet;
        private int m_selectedTileIndex;

        private LayerCompositing m_layerCompositing;
        private bool m_tileGuides;
        private EditTool m_editTool;
        private bool m_mouseInside;
        private Tiling.Location m_mouseLocation;
        private Tiling.Location m_tileDisplayLocation;
        private Tiling.Location m_tileLayerLocation;
        private Tiling.Location m_dragTileStart;

        private Graphics m_graphics;
        private Tiling.Rectangle m_viewPort;
        private int m_zoom;
        private Brush m_veilBrush;
        private ImageAttributes m_imageAttributes;
        private ColorMatrix m_colorMatrix;
        private Pen m_tileSelectionPen;
        private Brush m_tileSelectionBrush;

        private Pen m_tileGuidePen;
        private float[] m_dashPattern;

        private Cursor m_singleTileCursor;
        private Cursor m_tileBlockCursor;
        private Cursor m_eraserCursor;
        private Cursor m_dropperCursor;

        private bool m_bMouseDown;

        #endregion

        #region Private Methods

        private Location ConvertViewportOffsetToLayerLocation(Location viewPortOffset)
        {
            Location layerLocation
                = m_selectedLayer.ConvertMapToLayerLocation(m_viewPort.Location);

            Tiling.Size tileSize = m_selectedLayer.TileSize;

            layerLocation.X += viewPortOffset.X / (tileSize.Width * m_zoom);
            layerLocation.Y += viewPortOffset.Y / (tileSize.Height * m_zoom);

            return layerLocation;
        }

        private void DrawSingleTile(MouseEventArgs mouseEventArgs)
        {
            if (m_selectedLayer == null)
                return;
            if (m_selectedTileSheet == null)
                return;
            if (m_selectedTileIndex < 0)
                return;

            if (m_selectedLayer.TileSize != m_selectedTileSheet.TileSize)
            {
                MessageBox.Show(this, "Incompatible tile size", "Layer Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!m_selectedLayer.IsValidTileLocation(m_tileLayerLocation))
                return;

            Tile oldTile = m_selectedLayer.Tiles[m_tileLayerLocation];

            if (oldTile != null && oldTile.TileSheet == m_selectedTileSheet
                && oldTile.TileIndex == m_selectedTileIndex)
                return;

            Tile newTile = new StaticTile(m_selectedLayer, m_selectedTileSheet, BlendMode.Alpha, m_selectedTileIndex);
            m_selectedLayer.Tiles[m_tileLayerLocation] = newTile;

            m_innerPanel.Invalidate();        
        }

        private void DrawTileBlock()
        {
            if (m_selectedLayer == null)
                return;
            if (m_selectedTileSheet == null)
                return;
            if (m_selectedTileIndex < 0)
                return;

            if (m_selectedLayer.TileSize != m_selectedTileSheet.TileSize)
            {
                MessageBox.Show(this, "Incompatible tile size", "Layer Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Tiling.Size layerSize = m_selectedLayer.LayerSize;
            int minX = Math.Max(0, Math.Min(m_tileLayerLocation.X, m_dragTileStart.X));
            int minY = Math.Max(0, Math.Min(m_tileLayerLocation.Y, m_dragTileStart.Y));
            int maxX = Math.Min(layerSize.Width - 1, Math.Max(m_tileLayerLocation.X, m_dragTileStart.X));
            int maxY = Math.Min(layerSize.Height - 1, Math.Max(m_tileLayerLocation.Y, m_dragTileStart.Y));

            Tile newTile = new StaticTile(m_selectedLayer, m_selectedTileSheet, BlendMode.Alpha, m_selectedTileIndex);

            for (int tileY = minY; tileY <= maxY; tileY++)
                for (int tileX = minX; tileX <= maxX; tileX++)
                    m_selectedLayer.Tiles[tileX, tileY] = newTile;

            m_innerPanel.Invalidate();
        }

        private void EraseTile(MouseEventArgs mouseEventArgs)
        {
            if (m_selectedLayer == null)
                return;

            if (!m_selectedLayer.IsValidTileLocation(m_tileLayerLocation))
                return;

            if (m_selectedLayer.Tiles[m_tileLayerLocation] != null)
            {
                m_selectedLayer.Tiles[m_tileLayerLocation] = null;
                m_innerPanel.Invalidate();
            }
        }

        private void PickTile(MouseEventArgs mouseEventArgs)
        {
            if (m_selectedLayer == null)
                return;

            if (!m_selectedLayer.IsValidTileLocation(m_tileLayerLocation))
                return;

            Tile tile = m_selectedLayer.Tiles[m_tileLayerLocation];
            if (TilePicked != null)
            {
                this.EditTool = EditTool.SingleTile;
                TilePicked(new MapPanelEventArgs(tile));
            }
        }

        private void UpdateScrollBars()
        {
            if (m_map == null)
            {
                m_horizontalScrollBar.Maximum = 0;
                m_horizontalScrollBar.LargeChange = 1;
                m_horizontalScrollBar.Value = 0;
                m_horizontalScrollBar.Visible = false;

                m_verticalScrollBar.Maximum = 0;
                m_verticalScrollBar.LargeChange = 1;
                m_verticalScrollBar.Value = 0;
                m_verticalScrollBar.Visible = false;
            }
            else
            {
                System.Drawing.Rectangle clientRectangle = m_innerPanel.ClientRectangle;
                Tiling.Size displaySize = m_map.DisplaySize;

                m_horizontalScrollBar.Maximum = displaySize.Width;
                m_horizontalScrollBar.LargeChange = 1 + (clientRectangle.Width - 1) / m_zoom;
                m_horizontalScrollBar.Value
                    = Math.Min(m_horizontalScrollBar.Value, displaySize.Width);
                m_horizontalScrollBar.Visible = displaySize.Width > clientRectangle.Width;

                m_verticalScrollBar.Maximum = displaySize.Height;
                m_verticalScrollBar.LargeChange = 1 + (clientRectangle.Height - 1) / m_zoom;
                m_verticalScrollBar.Value
                    = Math.Min(m_verticalScrollBar.Value, displaySize.Height);
                m_verticalScrollBar.Visible = displaySize.Height > clientRectangle.Height;
            }
        }

        private void BindLayerDrawEvents()
        {
            if (m_map == null)
                return;

            foreach (Layer layer in m_map.Layers)
            {
                layer.BeforeDraw -= OnBeforeLayerDraw;
                layer.BeforeDraw += OnBeforeLayerDraw;

                layer.AfterDraw -= OnAfterLayerDraw;
                layer.AfterDraw += OnAfterLayerDraw;
            }
        }

        private void OnHorizontalScroll(object sender, ScrollEventArgs scrollEventArgs)
        {
            m_viewPort.Location.X = scrollEventArgs.NewValue;
            m_innerPanel.Invalidate();
        }

        private void OnVerticalScroll(object sender, ScrollEventArgs scrollEventArgs)
        {
            m_viewPort.Location.Y = scrollEventArgs.NewValue;
            m_innerPanel.Invalidate();
        }

        private void OnResizeDisplay(object sender, EventArgs e)
        {
            System.Drawing.Rectangle clientRectangle = m_innerPanel.ClientRectangle;
            m_viewPort.Size.Width = 1 + (clientRectangle.Width - 1)/ m_zoom;
            m_viewPort.Size.Height = 1 + (clientRectangle.Height - 1) / m_zoom;

            UpdateScrollBars();
        }

        private void OnBeforeLayerDraw(LayerEventArgs layerEventArgs)
        {
            if (m_layerCompositing == LayerCompositing.DimUnselected
                && layerEventArgs.Layer == m_selectedLayer
                && m_map.Layers.IndexOf(layerEventArgs.Layer) > 0)
            {
                m_graphics.FillRectangle(m_veilBrush, ClientRectangle);
            }
        }

        private void OnAfterLayerDraw(LayerEventArgs layerEventArgs)
        {
            if (layerEventArgs.Layer != m_selectedLayer)
                return;

            if (m_layerCompositing == LayerCompositing.DimUnselected)
            {
                // set translucency for upper layers
                m_colorMatrix.Matrix33 = 0.25f;
                m_imageAttributes.SetColorMatrix(m_colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            }

            // alignment data
            Layer layer = layerEventArgs.Layer;
            Tiling.Size layerDisplaySize = layer.DisplaySize;
            Tiling.Size mapDisplaySize = layer.Map.DisplaySize;
            Tiling.Rectangle viewPort = layerEventArgs.ViewPort;
            Tiling.Size tileSize = layer.TileSize;
            Tiling.Location scaledLocation = new Location(
                (viewPort.Location.X * layerDisplaySize.Width) / mapDisplaySize.Width,
                (viewPort.Location.Y * layerDisplaySize.Height) / mapDisplaySize.Height);
            int offsetX = scaledLocation.X % tileSize.Width;
            int offsetY = scaledLocation.Y % tileSize.Height;

            // tile guide
            if (m_tileGuides)
            {
                for (int guideY = -offsetY; guideY < viewPort.Size.Height; guideY += tileSize.Height)
                    m_graphics.DrawLine(m_tileGuidePen, 0, guideY, m_viewPort.Size.Width, guideY);

                for (int guideX = -offsetX; guideX < viewPort.Size.Width; guideX += tileSize.Width)
                    m_graphics.DrawLine(m_tileGuidePen, guideX, 0, guideX, m_viewPort.Size.Height);
            }

            // highlight tile under mouse cursor
            if (m_mouseInside)
            {
                int tileOffsetX = (m_mouseLocation.X + offsetX) / (tileSize.Width * m_zoom);
                int tileOffsetY = (m_mouseLocation.Y + offsetY) / (tileSize.Height * m_zoom);

                m_tileDisplayLocation.X = tileOffsetX * tileSize.Width - offsetX;
                m_tileDisplayLocation.Y = tileOffsetY * tileSize.Height - offsetY;

                int selectionX = 0, selectionY = 0;
                int selectionWidth = 0, selectionHeight = 0;
                if (m_editTool == EditTool.TileBlock && m_bMouseDown)
                {
                    int deltaTileX = m_tileLayerLocation.X - m_dragTileStart.X;
                    int deltaTileY = m_tileLayerLocation.Y - m_dragTileStart.Y;

                    selectionWidth = (Math.Abs(deltaTileX) + 1) * tileSize.Width;
                    selectionHeight = (Math.Abs(deltaTileY) + 1) * tileSize.Height;

                    if (deltaTileX >= 0)
                        selectionX = m_tileDisplayLocation.X - selectionWidth + tileSize.Width;
                    else
                        selectionX = m_tileDisplayLocation.X;

                    if (deltaTileY >= 0)
                        selectionY = m_tileDisplayLocation.Y - selectionHeight + tileSize.Height;
                    else
                        selectionY = m_tileDisplayLocation.Y;
                }
                else
                {
                    selectionX = m_tileDisplayLocation.X;
                    selectionY = m_tileDisplayLocation.Y;
                    selectionWidth = tileSize.Width;
                    selectionHeight = tileSize.Height;
                }

                m_graphics.FillRectangle(m_tileSelectionBrush,
                    selectionX, selectionY, selectionWidth, selectionHeight);
                m_graphics.DrawRectangle(m_tileSelectionPen,
                    selectionX, selectionY, selectionWidth, selectionHeight);
            }
        }

        private void OnMapPaint(object sender, PaintEventArgs paintEventArgs)
        {
            m_graphics = paintEventArgs.Graphics;

            if (m_map != null)
            {

                UpdateScrollBars();
                BindLayerDrawEvents();

                m_map.Draw(this, m_viewPort);

                // reset translucency
                m_colorMatrix.Matrix33 = 1.0f;
                m_imageAttributes.SetColorMatrix(m_colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

            }

            if (!Enabled)
            {
                m_graphics.Transform = new Matrix();
                m_graphics.FillRectangle(new SolidBrush(Color.FromArgb(224, SystemColors.Control)), ClientRectangle);
                SizeF stringSize = m_graphics.MeasureString("Add layers to this map", this.Font);
                m_graphics.DrawString("Add layers to this map", this.Font, SystemBrushes.ControlDark,
                    (ClientRectangle.Width - (int)stringSize.Width) / 2,
                    (ClientRectangle.Height - (int)stringSize.Height) / 2);
            }
        }

        private void OnMouseDown(object sender, MouseEventArgs mouseEventArgs)
        {
            m_bMouseDown = true;
            if (mouseEventArgs.Button == MouseButtons.Left)
            {
                switch (m_editTool)
                {
                    case EditTool.SingleTile: DrawSingleTile(mouseEventArgs); break;
                    case EditTool.TileBlock: m_dragTileStart = m_tileLayerLocation; break;
                    case EditTool.Eraser: EraseTile(mouseEventArgs); break;
                    case EditTool.Dropper: PickTile(mouseEventArgs); break;
                }
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs mouseEventArgs)
        {
            m_mouseInside = true;
            m_mouseLocation.X = mouseEventArgs.X;
            m_mouseLocation.Y = mouseEventArgs.Y;

            m_tileLayerLocation
                = ConvertViewportOffsetToLayerLocation(m_mouseLocation);

            if (m_bMouseDown && mouseEventArgs.Button == MouseButtons.Left)
            {
                switch (m_editTool)
                {
                    case EditTool.SingleTile: DrawSingleTile(mouseEventArgs); break;
                    case EditTool.Eraser: EraseTile(mouseEventArgs); break;
                    case EditTool.Dropper: PickTile(mouseEventArgs); break;
                }
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs mouseEventArgs)
        {
            if (mouseEventArgs.Button == MouseButtons.Left)
            {
                if (m_editTool == EditTool.TileBlock)
                {
                    DrawTileBlock();
                }
            }

            m_bMouseDown = false;
        }

        private void OnMouseEnter(object sender, EventArgs eventArgs)
        {
            m_mouseInside = true;
        }

        private void OnMouseLeave(object sender, EventArgs eventArgs)
        {
            m_mouseInside = false;
        }

        private void OnAnimationTimer(object sender, EventArgs eventArgs)
        {
            m_innerPanel.Invalidate();
        }

        #endregion

        #region Public Methods

        public MapPanel()
        {
            InitializeComponent();

            m_singleTileCursor = new Cursor(new MemoryStream(Properties.Resources.EditSingleTileCursor));
            m_tileBlockCursor = new Cursor(new MemoryStream(Properties.Resources.EditTileBlockCursor));
            m_eraserCursor = new Cursor(new MemoryStream(Properties.Resources.EditEraserCursor));
            m_dropperCursor = new Cursor(new MemoryStream(Properties.Resources.EditDropperCursor));

            m_viewPort = new Tiling.Rectangle(
                Tiling.Location.Origin, Tiling.Size.Zero);
            m_zoom = 1;

            m_layerCompositing = LayerCompositing.DimUnselected;

            m_editTool = EditTool.SingleTile;
            m_innerPanel.Cursor = m_singleTileCursor;
            m_mouseInside = false;
            m_mouseLocation = new Location();
            m_tileDisplayLocation = Tiling.Location.Origin;
            m_tileLayerLocation = Tiling.Location.Origin;
            m_dragTileStart = Tiling.Location.Origin;
            
            m_tileGuides = false;

            m_veilBrush = new SolidBrush(Color.FromArgb(192, SystemColors.InactiveCaption));
            m_imageAttributes = new ImageAttributes();
            m_colorMatrix = new ColorMatrix();
            m_tileSelectionPen = new Pen(SystemColors.ActiveCaption);
            m_tileSelectionBrush = new SolidBrush(
                Color.FromArgb(128, SystemColors.ActiveCaption));

            m_dashPattern = new float[] { 1.0f, 1.0f, 1.0f, 1.0f };
            m_tileGuidePen = new Pen(Color.Black);
            m_tileGuidePen.DashPattern = m_dashPattern;
        }

        public void LoadTileSheet(TileSheet tileSheet)
        {
            TileImageCache.Instance.Refresh(tileSheet);
        }

        public void DisposeTileSheet(TileSheet tileSheet)
        {
        }

        public void BeginScene()
        {
            m_graphics.ScaleTransform(m_zoom, m_zoom);
            m_graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            m_graphics.PixelOffsetMode = PixelOffsetMode.Half;
        }

        public void SetClippingRegion(Tiling.Rectangle clippingRegion)
        {
            if (m_graphics == null)
                return;

            m_graphics.SetClip(new RectangleF(
                    clippingRegion.Location.X, clippingRegion.Location.Y,
                    clippingRegion.Size.Width, clippingRegion.Size.Height));
        }

        public void DrawTile(Tile tile, Location location)
        {
            if (m_graphics == null)
                return;

            Bitmap tileBitmap = TileImageCache.Instance.GetTileBitmap(
                tile.TileSheet, tile.TileIndex);

            Tiling.Size tileSize = tile.TileSheet.TileSize;

            System.Drawing.Rectangle destRect = new System.Drawing.Rectangle(
                location.X, location.Y, tileSize.Width, tileSize.Height);

            m_graphics.DrawImage(tileBitmap, destRect,
                0, 0, tileSize.Width, tileSize.Height,
                GraphicsUnit.Pixel, m_imageAttributes);
        }

        public void EndScene()
        {
        }

        #endregion

        #region Public Properties

        [Description("The Map structure associated with this control"),
         Category("Data"), Browsable(true)
        ]
        public Map Map
        {
            get { return m_map; }
            set
            {
                m_map = value;

                if (m_map != null && !m_map.Layers.Contains(m_selectedLayer))
                    m_selectedLayer = null;

                UpdateScrollBars();

                Invalidate();
            }
        }

        [Description("The currently selected Layer"),
         Category("Data"), Browsable(true)
        ]
        public Layer SelectedLayer
        {
            get { return m_selectedLayer; }
            set
            {
                if (m_map == null)
                {
                    m_selectedLayer = null;
                    Invalidate(true);
                    return;
                }

                if (value == null)
                {
                    m_selectedLayer = null;
                    Invalidate(true);
                    return;
                }

                if (!m_map.Layers.Contains(value))
                    throw new Exception("The specified Layer is not contained in the Map");
                m_selectedLayer = value;
                Invalidate(true);
            }
        }

        [Description("The zoom level of the map display"),
         Category("Appearance"), Browsable(true), DefaultValue(1)
        ]
        public int Zoom
        {
            get { return m_zoom; }
            set
            {
                m_zoom = Math.Max(1, Math.Min(value, 10));

                System.Drawing.Rectangle clientRectangle = m_innerPanel.ClientRectangle;
                m_viewPort.Size.Width = 1 + (clientRectangle.Width - 1) / m_zoom;
                m_viewPort.Size.Height = 1 + (clientRectangle.Height - 1) / m_zoom;

                m_tileGuidePen.Width = m_tileSelectionPen.Width = 1.0f / m_zoom;
                m_dashPattern[0] = m_dashPattern[1] = m_dashPattern[2] = m_dashPattern[3] = 1.0f / m_zoom;
                m_tileGuidePen.DashPattern = m_dashPattern;

                Invalidate(true);
            }
        }

        [Description("The layer compositing mode for the map display"),
         Category("Appearance"), DefaultValue(LayerCompositing.DimUnselected)]
        public LayerCompositing LayerCompositing
        {
            get { return m_layerCompositing; }
            set
            {
                if (m_layerCompositing != value)
                {
                    m_layerCompositing = value;
                    m_innerPanel.Invalidate();
                }
            }
        }

        [Description("Show or hide the tile guides to assist editing"),
         Category("Appearance"), DefaultValue(false)]
        public bool TileGuides
        {
            get { return m_tileGuides; }
            set
            {
                if (m_tileGuides != value)
                {
                    m_tileGuides = value;
                    m_innerPanel.Invalidate();
                }
            }
        }

        [Description("The current editing tool"),
         Category("Behavior"), DefaultValue(EditTool.TileBlock)]
        public EditTool EditTool
        {
            get { return m_editTool; }
            set
            {
                m_editTool = value;
                switch (m_editTool)
                {
                    case EditTool.SingleTile: m_innerPanel.Cursor = m_singleTileCursor; break;
                    case EditTool.TileBlock: m_innerPanel.Cursor = m_tileBlockCursor; break;
                    case EditTool.Eraser: m_innerPanel.Cursor = m_eraserCursor; break;
                    case EditTool.Dropper: m_innerPanel.Cursor = m_dropperCursor; break;
                }
            }
        }

        public TileSheet SelectedTileSheet
        {
            get { return m_selectedTileSheet; }
            set { m_selectedTileSheet = value; }
        }

        public int SelectedTileIndex
        {
            get { return m_selectedTileIndex; }
            set { m_selectedTileIndex = value; }
        }

        #endregion

        #region Public Events

        [Category("Behavior"), Description("Occurs when the tile is picked from the map")]
        public event MapPanelEventHandler TilePicked;

        #endregion
    }

    public enum LayerCompositing
    {
        DimUnselected,
        ShowAll
    }

    public enum EditTool
    {
        SingleTile,
        TileBlock,
        Eraser,
        Dropper
    }

    public delegate void MapPanelEventHandler(MapPanelEventArgs mapPanelEventArgs);
}
