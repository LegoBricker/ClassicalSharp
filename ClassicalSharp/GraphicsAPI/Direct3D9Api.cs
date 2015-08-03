﻿#if USE_DX
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using SharpDX;
using SharpDX.Direct3D9;
using D3D = SharpDX.Direct3D9;
using Matrix4 = OpenTK.Matrix4;
using WinWindowInfo = OpenTK.Platform.Windows.WinWindowInfo;

namespace ClassicalSharp.GraphicsAPI {

	// TODO: Should we use a native form wrapper instead of wrapping over OpenTK?
	public class Direct3D9Api : IGraphicsApi {

		Device device;
		Direct3D d3d;
		Capabilities caps;
		const int texBufferSize = 512, iBufferSize = 1024, vBufferSize = 2048;
		
		D3D.Texture[] textures = new D3D.Texture[texBufferSize];
		VertexBuffer[] vBuffers = new VertexBuffer[vBufferSize];
		IndexBuffer[] iBuffers = new IndexBuffer[iBufferSize];
		MatrixStack viewStack, projStack, texStack;
		MatrixStack curStack;
		PrimitiveType[] modeMappings = {
			PrimitiveType.TriangleList, PrimitiveType.LineList,
			PrimitiveType.TriangleStrip,
		};
		static Format[] depthFormats = { Format.D32, Format.D24X8, Format.D24S8, Format.D24X4S4, Format.D16, Format.D15S1 };
		Format depthFormat, viewFormat = Format.X8R8G8B8;
		bool memcpy64Bit;
		CreateFlags createFlags = CreateFlags.HardwareVertexProcessing;

		public Direct3D9Api( Game game ) {
			IntPtr windowHandle = ((WinWindowInfo)game.WindowInfo).WindowHandle;
			d3d = new Direct3D();
			int adapter = d3d.Adapters[0].Adapter;
			
			for( int i = 0; i < depthFormats.Length; i++ ) {
				depthFormat = depthFormats[i];
				if( d3d.CheckDepthStencilMatch( adapter, DeviceType.Hardware, viewFormat, viewFormat, depthFormat ) ) break;
				
				if( i == depthFormats.Length - 1 )
					throw new InvalidOperationException( "Unable to create a depth buffer with sufficient precision." );
			}
			
			PresentParameters args = GetPresentArgs( 640, 480 );
			try {
				device = new Device( d3d, adapter, DeviceType.Hardware, windowHandle, createFlags, args );
			} catch( SharpDXException ) {
				createFlags = CreateFlags.MixedVertexProcessing;
				try {
					device = new Device( d3d, adapter, DeviceType.Hardware, windowHandle, createFlags, args );
				} catch ( SharpDXException ) {
					createFlags = CreateFlags.SoftwareVertexProcessing;
					device = new Device( d3d, adapter, DeviceType.Hardware, windowHandle, createFlags, args );
				}
			}
			
			caps = device.Capabilities;
			viewStack = new MatrixStack( 32, device, TransformState.View );
			projStack = new MatrixStack( 4, device, TransformState.Projection );
			texStack = new MatrixStack( 4, device, TransformState.Texture0 );
			SetDefaultRenderStates();
			memcpy64Bit = IntPtr.Size == 8;
		}
		
		bool alphaTest, alphaBlend;
		public override bool AlphaTest {
			set { if( value == alphaTest ) return;
				alphaTest = value; device.SetRenderState( RenderState.AlphaTestEnable, value );
			}
		}

		public override bool AlphaBlending {
			set { if( value == alphaBlend ) return;
				alphaBlend = value; device.SetRenderState( RenderState.AlphaBlendEnable, value );
			}
		}

		Compare[] compareFuncs = {
			Compare.Always, Compare.NotEqual, Compare.Never, Compare.Less,
			Compare.LessEqual, Compare.Equal, Compare.GreaterEqual, Compare.Greater,
		};
		Compare alphaTestFunc;
		int alphaTestRef;
		public override void AlphaTestFunc( CompareFunc func, float value ) {
			alphaTestFunc = compareFuncs[(int)func];
			device.SetRenderState( RenderState.AlphaFunc, (int)alphaTestFunc );
			alphaTestRef = (int)( value * 255 );
			device.SetRenderState( RenderState.AlphaRef, alphaTestRef );
		}

		Blend[] blendFuncs = {
			Blend.Zero, Blend.One,
			Blend.SourceAlpha, Blend.InverseSourceAlpha,
			Blend.DestinationAlpha, Blend.InverseDestinationAlpha,
		};
		Blend srcFunc, dstFunc;
		public override void AlphaBlendFunc( BlendFunc srcBlendFunc, BlendFunc dstBlendFunc ) {
			srcFunc = blendFuncs[(int)srcBlendFunc];
			dstFunc = blendFuncs[(int)dstBlendFunc];
			device.SetRenderState( RenderState.SourceBlend, (int)srcFunc );
			device.SetRenderState( RenderState.DestinationBlend, (int)dstFunc );
		}

		bool fogEnable;
		public override bool Fog {
			set { if( value == fogEnable ) return;
				fogEnable = value; device.SetRenderState( RenderState.FogEnable, value );
			}
		}

		int fogCol;
		public override void SetFogColour( FastColour col ) {
			fogCol = col.ToArgb();
			device.SetRenderState( RenderState.FogColor, fogCol );
		}

		float fogDensity = -1, fogStart = -1, fogEnd = -1;
		public override void SetFogDensity( float value ) {
			if( value == fogDensity ) return;
			fogDensity = value;
			device.SetRenderState( RenderState.FogDensity, value );
		}
		
		public override void SetFogStart( float value ) {
			if( value == fogStart ) return;
			fogStart = value;
			device.SetRenderState( RenderState.FogStart, value );
		}

		public override void SetFogEnd( float value ) {
			if( value == fogEnd ) return;
			fogEnd = value;
			device.SetRenderState( RenderState.FogEnd, value );
		}

		FogMode[] modes = { FogMode.Linear, FogMode.Exponential, FogMode.ExponentialSquared };
		FogMode fogMode;
		public override void SetFogMode( Fog mode ) {
			FogMode newMode = modes[(int)mode];
			if( newMode == fogMode ) return;
			fogMode = newMode;
			device.SetRenderState( RenderState.FogTableMode, (int)fogMode );
		}
		
		public override bool FaceCulling {
			set {
				Cull mode = value ? Cull.Clockwise : Cull.None;
				device.SetRenderState( RenderState.CullMode, (int)mode );
			}
		}

		public override int MaxTextureDimensions {
			get { return Math.Min( caps.MaxTextureHeight, caps.MaxTextureWidth ); }
		}

		public override int LoadTexture( int width, int height, IntPtr scan0 ) {
			D3D.Texture texture = new D3D.Texture( device, width, height, 0, Usage.None, Format.A8R8G8B8, Pool.Managed );
			LockedRectangle vbData = texture.LockRectangle( 0, LockFlags.None );
			IntPtr dest = vbData.DataPointer;
			memcpy( scan0, dest, width * height * 4 );
			texture.UnlockRectangle( 0 );
			return GetOrExpand( ref textures, texture, texBufferSize );
		}

		public override void Bind2DTexture( int texId ) {
			device.SetTexture( 0, textures[texId] );
		}

		public override bool Texturing {
			set { if( !value ) device.SetTexture( 0, null ); }
		}

		public override void DeleteTexture( ref int texId ) {
			Delete( textures, texId );
			texId = -1;
		}
		
		public override bool IsValidTexture( int texId ) {
			return texId < textures.Length && textures[texId] != null;
		}

		int lastClearCol;
		public override void Clear() {
			device.Clear( ClearFlags.Target | ClearFlags.ZBuffer, lastClearCol, 1f, 0 );
		}

		public override void ClearColour( FastColour col ) {
			lastClearCol = col.ToArgb();
		}

		public override bool ColourWrite {
			set { device.SetRenderState( RenderState.ColorWriteEnable, value ? 0xF : 0x0 ); }
		}

		Compare depthTestFunc;
		public override void DepthTestFunc( CompareFunc func ) {
			depthTestFunc = compareFuncs[(int)func];
			device.SetRenderState( RenderState.ZFunc, (int)depthTestFunc );
		}

		bool depthTest, depthWrite;
		public override bool DepthTest {
			set { depthTest = value; device.SetRenderState( RenderState.ZEnable, value ); }
		}

		public override bool DepthWrite {
			set { depthWrite = value; device.SetRenderState( RenderState.ZWriteEnable, value ); }
		}
		
		public override int CreateDynamicVb( VertexFormat format, int maxVertices ) {
			return -1;
		}
		
		public override void DrawDynamicVb<T>( DrawMode mode, int vb, T[] vertices, VertexFormat format, int count ) {
			device.SetVertexFormat( formatMapping[(int)format] );
			device.DrawUserPrimitives( modeMappings[(int)mode], 0, NumPrimitives( count, mode ), vertices );
		}
		
		public override void DeleteDynamicVb( int id ) {
		}

		#region Vertex buffers

		D3D.VertexFormat[] formatMapping = {
			D3D.VertexFormat.Position | D3D.VertexFormat.Texture2,
			D3D.VertexFormat.Position | D3D.VertexFormat.Diffuse,
			D3D.VertexFormat.Position | D3D.VertexFormat.Texture2 | D3D.VertexFormat.Diffuse,
		};

		public override int InitVb<T>( T[] vertices, VertexFormat format, int count ) {
			VertexBuffer buffer = CreateVb( vertices, count, format );
			return GetOrExpand( ref vBuffers, buffer, vBufferSize );
		}
		
		public override int InitIb( ushort[] indices, int count ) {
			IndexBuffer buffer = CreateIb( indices, count );
			return GetOrExpand( ref iBuffers, buffer, iBufferSize );
		}

		unsafe VertexBuffer CreateVb<T>( T[] vertices, int count, VertexFormat format ) {
			int sizeInBytes = count * strideSizes[(int)format];
			D3D.VertexFormat d3dFormat = formatMapping[(int)format];
			VertexBuffer buffer = new VertexBuffer( device, sizeInBytes, Usage.None, d3dFormat, Pool.Managed );
			
			IntPtr vbData = buffer.Lock( 0, sizeInBytes, LockFlags.None );
			GCHandle handle = GCHandle.Alloc( vertices, GCHandleType.Pinned );
			IntPtr source = handle.AddrOfPinnedObject();
			memcpy( source, vbData, sizeInBytes );
			buffer.Unlock();
			handle.Free();
			return buffer;
		}
		
		unsafe IndexBuffer CreateIb( ushort[] indices, int count ) {
			int sizeInBytes = count * 2;
			IndexBuffer buffer = new IndexBuffer( device, sizeInBytes, Usage.None, Pool.Managed, true );
			
			IntPtr vbData = buffer.Lock( 0, sizeInBytes, LockFlags.None );
			fixed( ushort* src = indices ) {
				memcpy( (IntPtr)src, vbData, sizeInBytes );
			}
			buffer.Unlock();
			return buffer;
		}

		public override void DeleteVb( int vb ) {
			Delete( vBuffers, vb );
		}
		
		public override void DeleteIb( int ib ) {
			Delete( iBuffers, ib );
		}
		
		public override bool IsValidVb( int vb ) {
			return IsValid( vBuffers, vb );
		}
		
		public override bool IsValidIb( int ib ) {
			return IsValid( iBuffers, ib );
		}

		public override void DrawVb( DrawMode mode, VertexFormat format, int id, int startVertex, int verticesCount ) {
			device.SetStreamSource( 0, vBuffers[id], 0, strideSizes[(int)format] );
			device.SetVertexFormat( formatMapping[(int)format] );
			device.DrawPrimitives( modeMappings[(int)mode], startVertex, NumPrimitives( verticesCount, mode ) );
		}

		int batchStride;
		public override void BeginVbBatch( VertexFormat format ) {
			device.SetVertexFormat( formatMapping[(int)format] );
			batchStride = strideSizes[(int)format];
		}

		public override void DrawVbBatch( DrawMode mode, int id, int startVertex, int verticesCount ) {
			device.SetStreamSource( 0, vBuffers[id], 0, batchStride );
			device.DrawPrimitives( modeMappings[(int)mode], startVertex, NumPrimitives( verticesCount, mode ) );
		}
		
		public override void BeginIndexedVbBatch() {
			device.SetVertexFormat( formatMapping[(int)VertexFormat.Pos3fTex2fCol4b] );
			batchStride = VertexPos3fTex2fCol4b.Size;
		}

		public override void DrawIndexedVbBatch( DrawMode mode, int vb, int ib, int indicesCount,
		                                        int startVertex, int startIndex ) {
			device.SetIndices( iBuffers[ib] );
			device.SetStreamSource( 0, vBuffers[vb], 0, batchStride );
			device.DrawIndexedPrimitives( modeMappings[(int)mode], startVertex, startVertex,
			                             indicesCount / 6 * 4, startIndex, NumPrimitives( indicesCount, mode ) );
		}

		public override void EndIndexedVbBatch() {
			device.SetIndices( null );
		}

		#endregion


		#region Matrix manipulation

		public override void SetMatrixMode( MatrixType mode ) {
			if( mode == MatrixType.Modelview ) {
				curStack = viewStack;
			} else if( mode == MatrixType.Projection ) {
				curStack = projStack;
			} else if( mode == MatrixType.Texture ) {
				curStack = texStack;
			}
		}

		public unsafe override void LoadMatrix( ref Matrix4 matrix ) {
			Matrix4 transposed = matrix;
			Matrix dxMatrix = *(Matrix*)&transposed;
			if( curStack == texStack ) {
				dxMatrix.M31 = dxMatrix.M41; // NOTE: this hack fixes the texture movements.
				device.SetTextureStageState( 0, TextureStage.TextureTransformFlags, (int)TextureTransform.Count2 );
			}
			curStack.SetTop( ref dxMatrix );
		}

		Matrix identity = Matrix.Identity;
		public override void LoadIdentityMatrix() {
			if( curStack == texStack ) {
				device.SetTextureStageState( 0, TextureStage.TextureTransformFlags, (int)TextureTransform.Disable );
			}
			curStack.SetTop( ref identity );
		}

		public override void PushMatrix() {
			curStack.Push();
		}

		public override void PopMatrix() {
			curStack.Pop();
		}

		public unsafe override void MultiplyMatrix( ref Matrix4 matrix ) {
			Matrix4 transposed = matrix;
			Matrix dxMatrix = *(Matrix*)&transposed;
			curStack.MultiplyTop( ref dxMatrix );
		}

		class MatrixStack
		{
			Matrix[] stack;
			int stackIndex;
			Device device;
			TransformState matrixType;

			public MatrixStack( int capacity, Device device, TransformState matrixType ) {
				stack = new Matrix[capacity];
				stack[0] = Matrix.Identity;
				this.device = device;
				this.matrixType = matrixType;
			}

			public void Push() {
				stack[stackIndex + 1] = stack[stackIndex]; // mimic GL behaviour
				stackIndex++; // exact same, we don't need to update DirectX state.
			}

			public void SetTop( ref Matrix matrix ) {
				stack[stackIndex] = matrix;
				device.SetTransform( matrixType, ref stack[stackIndex] );
			}

			public void MultiplyTop( ref Matrix matrix ) {
				stack[stackIndex] = matrix * stack[stackIndex];
				device.SetTransform( matrixType, ref stack[stackIndex] );
			}

			public Matrix GetTop() {
				return stack[stackIndex];
			}

			public void Pop() {
				stackIndex--;
				device.SetTransform( matrixType, ref stack[stackIndex] );
			}
		}

		#endregion
		
		public override void BeginFrame( Game game ) {
			device.BeginScene();
		}
		
		public override void EndFrame( Game game ) {
			device.EndScene();
			int code = device.Present().Code;
			if( code >= 0 ) return;
			
			if( (uint)code != (uint)Direct3DError.DeviceLost )
				throw new SharpDXException( code );
			
			// TODO: Make sure this actually works on all graphics cards.
			Utils.LogDebug( "Lost Direct3D device." );
			while( true ) {
				Thread.Sleep( 50 );
				code = device.TestCooperativeLevel().Code;
				if( (uint)code == (uint)Direct3DError.DeviceNotReset ) {
					Utils.Log( "Retrieved Direct3D device again." );
					RecreateDevice( game );
					break;
				}
				game.Network.Tick( 1 / 20.0 );
			}
		}
		
		bool vsync = false;
		public override void SetVSync( Game game, bool value ) {
			vsync = value;
			game.VSync = value;
			RecreateDevice( game );
		}
		
		public override void OnWindowResize( Game game ) {
			RecreateDevice( game );
		}
		
		void RecreateDevice( Game game ) {
			PresentParameters args = GetPresentArgs( game.Width, game.Height );
			device.Reset( args );
			SetDefaultRenderStates();
			device.SetRenderState( RenderState.AlphaTestEnable, alphaTest );
			device.SetRenderState( RenderState.AlphaBlendEnable, alphaBlend );
			device.SetRenderState( RenderState.AlphaFunc, (int)alphaTestFunc );
			device.SetRenderState( RenderState.AlphaRef, alphaTestRef );
			device.SetRenderState( RenderState.SourceBlend, (int)srcFunc );
			device.SetRenderState( RenderState.DestinationBlend, (int)dstFunc );
			device.SetRenderState( RenderState.FogEnable, fogEnable );
			device.SetRenderState( RenderState.FogColor, fogCol );
			device.SetRenderState( RenderState.FogDensity, fogDensity );
			device.SetRenderState( RenderState.FogStart, fogStart );
			device.SetRenderState( RenderState.FogEnd, fogEnd );
			device.SetRenderState( RenderState.FogTableMode, (int)fogMode );
			device.SetRenderState( RenderState.ZFunc, (int)depthTestFunc );
			device.SetRenderState( RenderState.ZEnable, depthTest );
			device.SetRenderState( RenderState.ZWriteEnable, depthWrite );
		}
		
		void SetDefaultRenderStates() {
			device.SetRenderState( RenderState.FillMode, (int)FillMode.Solid );
			FaceCulling = false;
			device.SetRenderState( RenderState.ColorVertex, false );
			device.SetRenderState( RenderState.Lighting, false );
			device.SetRenderState( RenderState.SpecularEnable, false );
			device.SetRenderState( RenderState.LocalViewer, false );
			device.SetRenderState( RenderState.DebugMonitorToken, false );
		}
		
		PresentParameters GetPresentArgs( int width, int height ) {
			PresentParameters args = new PresentParameters();
			args.AutoDepthStencilFormat = depthFormat;
			args.BackBufferWidth = width;
			args.BackBufferHeight = height;
			args.BackBufferFormat = viewFormat;
			args.BackBufferCount = 1;
			args.EnableAutoDepthStencil = true;
			args.PresentationInterval = vsync ? PresentInterval.One : PresentInterval.Immediate;
			args.SwapEffect = SwapEffect.Discard;
			args.Windowed = true;
			return args;
		}
		
		unsafe void memcpy( IntPtr srcPtr, IntPtr dstPtr, int bytes ) {
			byte* srcByte, dstByte;
			if( memcpy64Bit ) {
				long* srcLong = (long*)srcPtr, dstLong = (long*)dstPtr;
				while( bytes >= 8 ) {
					*dstLong++ = *srcLong++;
					bytes -= 8;
				}
				srcByte = (byte*)srcLong; dstByte = (byte*)dstLong;
			} else {
				int* srcInt = (int*)srcPtr, dstInt = (int*)dstPtr;
				while( bytes >= 4 ) {
					*dstInt++ = *srcInt++;
					bytes -= 4;
				}
				srcByte = (byte*)srcInt; dstByte = (byte*)dstInt;
			}
			for( int i = 0; i < bytes; i++ ) {
				*dstByte++ = *srcByte++;
			}
		}
		
		static int GetOrExpand<T>( ref T[] array, T value, int expSize ) {
			// Find first free slot
			for( int i = 1; i < array.Length; i++ ) {
				if( array[i] == null ) {
					array[i] = value;
					return i;
				}
			}
			// Otherwise resize and add more elements
			int oldLength = array.Length;
			Array.Resize( ref array, array.Length + expSize );
			array[oldLength] = value;
			return oldLength;
		}
		
		static void Delete<T>( T[] array, int id ) where T : class, IDisposable {
			if( id <= 0 || id >= array.Length ) return;
			
			T value = array[id];
			if( value != null ) {
				value.Dispose();
			}
			array[id] = null;
		}
		
		static int NumPrimitives( int vertices, DrawMode mode ) {
			if( mode == DrawMode.Triangles ) {
				return vertices / 3;
			} else if( mode == DrawMode.TriangleStrip ) {
				return vertices - 2;
			}
			return vertices / 2;
		}
		
		static bool IsValid<T>( T[] array, int id ) {
			return id > 0 && id < array.Length && array[id] != null;
		}
		
		protected unsafe override void LoadOrthoMatrix( float width, float height ) {
			Matrix4 matrix = Matrix4.CreateOrthographicOffCenter( 0, width, height, 0, 0, 1 );
			matrix.M33 = -1;
			matrix.M43 = 0;
			Matrix dxMatrix = *(Matrix*)&matrix;
			curStack.SetTop( ref dxMatrix );
		}
		
		public override void Dispose() {
			base.Dispose();
			device.Dispose();
			d3d.Dispose();
		}

		public override void PrintApiSpecificInfo() {
			Utils.Log( "D3D tex memory available: " + (uint)device.AvailableTextureMemory );
			Utils.Log( "D3D vertex processing: " + createFlags );
			Utils.Log( "D3D depth buffer format: " + depthFormat );
			Utils.Log( "D3D device caps: " + caps.DeviceCaps );
		}

		public unsafe override void TakeScreenshot( string output, Size size ) {
			using( Surface backbuffer = device.GetBackBuffer( 0, 0, BackBufferType.Mono ),
			      tempSurface = device.CreateOffscreenPlainSurface( size.Width, size.Height, Format.X8R8G8B8, Pool.SystemMemory ) ) {
				// For DX 8 use IDirect3DDevice8::CreateImageSurface
				device.GetRenderTargetData( backbuffer, tempSurface );
				LockedRectangle rect = tempSurface.LockRectangle( LockFlags.ReadOnly | LockFlags.NoDirtyUpdate );
				
				using( Bitmap bmp = new Bitmap( size.Width, size.Height, size.Width * 4,
				                               PixelFormat.Format32bppRgb, rect.DataPointer ) ) {
					bmp.Save( output, ImageFormat.Png );
				}
				tempSurface.UnlockRectangle();
			}
		}
	}
}
#endif