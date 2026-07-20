using System;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CaptureCamera : MonoBehaviour {
	private readonly int _width = 256;
	private readonly int _height = 256;
	
	private Camera _camera;
	public Camera Camera => _camera;
	
	private RenderTexture _renderTexture;
	private Texture2D _texture;
	
	public void Initialize() {
		_renderTexture = new RenderTexture(_width, _height, 24, RenderTextureFormat.ARGB32);
		_renderTexture.Create(); 	
		
		_texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false);	
		
		_camera ??= GetComponent<Camera>();
		
		_camera.clearFlags = CameraClearFlags.SolidColor;
		_camera.backgroundColor = Color.black;
	
		_camera.targetTexture = _renderTexture;
	}
	
	public byte[] Capture() {
		_camera.Render();
		RenderTexture.active = _renderTexture;
		_texture.ReadPixels(new Rect(0, 0, _renderTexture.width, _renderTexture.height), 0, 0);
		_texture.Apply(false, false);
		return _texture.EncodeToPNG();
	}
}