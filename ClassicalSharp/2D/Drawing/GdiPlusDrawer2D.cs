﻿#if !ANDROID
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ClassicalSharp {

	public sealed partial class GdiPlusDrawer2D : IDrawer2D {

		Dictionary<int, SolidBrush> brushCache = new Dictionary<int, SolidBrush>( 16 );		
		Graphics g;
		Bitmap curBmp;
		
		public override void SetBitmap( Bitmap bmp ) {
			if( g != null ) {
				Utils.LogDebug( "Previous IDrawer2D.SetBitmap() call was not properly disposed" );
				g.Dispose();
			}
			
			g = Graphics.FromImage( bmp );
			g.TextRenderingHint = TextRenderingHint.AntiAlias;
			g.SmoothingMode = SmoothingMode.HighQuality;
			curBmp = bmp;
		}
		
		public override void DrawRect( FastColour colour, int x, int y, int width, int height ) {
			Brush brush = GetOrCreateBrush( colour );
			g.FillRectangle( brush, x, y, width, height );
		}
		
		public override void DrawRectBounds( FastColour colour, float lineWidth, int x, int y, int width, int height ) {
			using( Pen pen = new Pen( colour, lineWidth ) ) {
				pen.Alignment = PenAlignment.Inset;
				g.DrawRectangle( pen, x, y, width, height );
			}
		}
		
		[Obsolete("Method is not guaranteed to work on all platforms.")]
		public override void DrawRoundedRect( FastColour colour, float radius, float x, float y, float width, float height ) {
			GraphicsPath path = new GraphicsPath();
			float x1 = x, y1 = y, x2 = x + width, y2 = y + height;
			
			float r = radius, dia = radius * 2;
			path.AddArc( x1, y1, dia, dia, 180, 90 );
			path.AddLine( x1 + r, y1, x2 - r, y1 );
			path.AddArc( x2 - dia, y1, dia, dia, 270, 90 );
			path.AddLine( x2, y1 + r, x2, y2 - r );
			path.AddArc( x2 - dia, y2 - dia, dia, dia, 0, 90 );
			path.AddLine( x1 + r, y2, x2 - r, y2 );
			path.AddArc( x1, y2 - dia, dia, dia, 90, 90 );
			path.AddLine( x1, y1 + r, x1, y2 - r );
			path.CloseAllFigures();
			
			using( Brush brush = new SolidBrush( colour ) )
				g.FillPath( brush, path );
			path.Dispose();
		}
		
		public override void Clear( FastColour colour ) {
			g.Clear( colour );
		}
		
		public override void Clear( FastColour colour, int x, int y, int width, int height ) {
			g.SmoothingMode = SmoothingMode.None;
			Brush brush = GetOrCreateBrush( colour );
			g.FillRectangle( brush, x, y, width, height );
			g.SmoothingMode = SmoothingMode.HighQuality;
		}
		
		public override void Dispose() {
			g.Dispose();
			g = null;
		}

		public override Bitmap ConvertTo32Bpp( Bitmap src ) {
			Bitmap bmp = new Bitmap( src.Width, src.Height );
			using( Graphics g = Graphics.FromImage( bmp ) ) {
				g.InterpolationMode = InterpolationMode.NearestNeighbor;
				g.DrawImage( src, 0, 0, src.Width, src.Height );
			}
			return bmp;
		}
		
		public override void DisposeInstance() {
			foreach( var pair in brushCache )
				pair.Value.Dispose();
			
			DisposeText();
			DisposeBitmappedText();
		}
		
		SolidBrush GetOrCreateBrush( Color color ) {
			int key = color.ToArgb();
			SolidBrush brush;
			if( brushCache.TryGetValue( key, out brush ) )
				return brush;
			
			brush = new SolidBrush( color );
			brushCache[key] = brush;
			return brush;
		}
	}
}
#endif