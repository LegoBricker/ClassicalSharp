﻿using System;
using System.Drawing;
using OpenTK.Input;

namespace ClassicalSharp {
	
	public abstract class MenuScreen : ClickableScreen {
		
		public MenuScreen( Game game ) : base( game ) {
		}
		protected Widget[] widgets;
		protected Font titleFont, regularFont;
		
		protected void RenderMenuBounds() {
			graphicsApi.Draw2DQuad( 0, 0, game.Width, game.Height, new FastColour( 60, 60, 60, 160 ) );
		}
		
		protected void RenderMenuWidgets( double delta ) {
			for( int i = 0; i < widgets.Length; i++ ) {
				if( widgets[i] == null ) continue;
				widgets[i].Render( delta );
			}
		}
		
		public override void Init() {
			int size = game.Drawer2D.UseBitmappedChat ? 13 : 16;
			titleFont = new Font( game.FontName, size, FontStyle.Bold );
		} 
		
		public override void Dispose() {
			for( int i = 0; i < widgets.Length; i++ ) {
				if( widgets[i] == null ) continue;
				widgets[i].Dispose();
			}
			titleFont.Dispose();
			if( regularFont != null )
				regularFont.Dispose();
		}

		public override void OnResize( int oldWidth, int oldHeight, int width, int height ) {
			for( int i = 0; i < widgets.Length; i++ ) {
				if( widgets[i] == null ) continue;
				widgets[i].OnResize( oldWidth, oldHeight, width, height );
			}
		}
		
		public override bool HandlesAllInput { get { return true; } }
		
		public override bool HandlesMouseClick( int mouseX, int mouseY, MouseButton button ) {
			return HandleMouseClick( widgets, mouseX, mouseY, button );
		}
		
		public override bool HandlesMouseMove( int mouseX, int mouseY ) {
			return HandleMouseMove( widgets, mouseX, mouseY );
		}
		
		public override bool HandlesKeyPress( char key ) { return true; }
		
		public override bool HandlesKeyDown( Key key ) { return true; }
		
		public override bool HandlesKeyUp( Key key ) { return true; }
	}
}