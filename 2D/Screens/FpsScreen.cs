﻿using System;

namespace ClassicalSharp {
	
	public class FpsScreen : Screen {
		
		public FpsScreen( Game window ) : base( window ) {
		}
		
		TextWidget fpsTextWidget;
		
		public override void Render( double delta ) {
			UpdateFPS( delta );
			fpsTextWidget.Render( delta );
		}
		
		const string formatString = "FPS: {0} (min {1}), chunks/s: {2}, vertices: {3}";
		double accumulator, maxDelta;
		int fpsCount;
		
		void UpdateFPS( double delta ) {
			fpsCount++;
			maxDelta = Math.Max( maxDelta, delta );
			accumulator += delta;
			long vertices = Window.Vertices;

			if( accumulator >= 1 )  {
				string text = String.Format( formatString, (int)( fpsCount / accumulator ),
				                            (int)( 1f / maxDelta ), Window.ChunkUpdates, vertices );
				fpsTextWidget.SetText( text );
				maxDelta = 0;
				accumulator = 0;
				fpsCount = 0;
				Window.ChunkUpdates = 0;
			}
		}
		
		public override void Init() {
			fpsTextWidget = new TextWidget( Window, 13 );
			fpsTextWidget.Init();
			fpsTextWidget.SetText( "FPS: no data yet" );
		}
		
		public override void Dispose() {
			fpsTextWidget.Dispose();
		}
	}
}