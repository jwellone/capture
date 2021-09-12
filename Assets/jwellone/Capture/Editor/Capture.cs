using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace jwellone.Editor
{
	public static class Capture
	{
		public delegate string MakeFilePathDelegate(string fileName);
		private static MakeFilePathDelegate s_makeFilePathDelegate = MakeUniquePath;

		public static MakeFilePathDelegate MakeFilePath
		{
			get => s_makeFilePathDelegate;
			set => s_makeFilePathDelegate = (value == null) ? MakeUniquePath : value;
		}

		public static Color SuccessColor
		{
			get;
			set;
		} = Color.cyan;

		[MenuItem("jwellone/Capture/Take/Active Window %#t")]
		public static void TakeActiveWindow()
		{
			TakeWinodw(EditorWindow.focusedWindow, MakeFilePath("ActiveWindow"));
		}

		static public void TakeActiveWindow(string filePath, Action<bool> callback = null)
		{
			TakeWinodw(EditorWindow.focusedWindow, filePath, callback);
		}

		public static void TakeWindow(Type type, string filePath, Action<bool> callback = null)
		{
			if (type == null)
			{
				callback?.Invoke(false);
				Debug.LogError($"[Capture]type is null. {filePath}");
				return;
			}

			TakeWinodw(EditorWindow.GetWindow(type), filePath, callback);
		}

		public static void TakeWinodw(EditorWindow window, string filePath, Action<bool> callback = null)
		{
			if (window == null)
			{
				callback?.Invoke(false);
				Debug.LogError($"[Capture]window is null. {filePath}");
				return;
			}

			window.Focus();

			EditorApplication.delayCall += () =>
			{
				TakePicture(window.position, filePath, callback);
			};
		}

		[MenuItem("jwellone/Capture/Take/Game View(x1)")]
		public static void TakeGameView()
		{
			TakeGameView(MakeFilePath("GameView"), 1);
		}

		[MenuItem("jwellone/Capture/Take/Game View(x2)")]
		public static void TakeGameView2()
		{
			TakeGameView(MakeFilePath("GameView"), 2);
		}

		public static void TakeGameView(string filePath, int superSize = 1)
		{
			ScreenCapture.CaptureScreenshot(filePath, superSize);
			var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
			EditorWindow.GetWindow(type).Repaint();
		}

		[MenuItem("jwellone/Capture/Take/Scene View")]
		public static void TakeSceneView()
		{
			TakeSceneView(MakeFilePath("SceneView"));
		}

		public static void TakeSceneView(string filePath)
		{
			if (SceneView.lastActiveSceneView == null)
			{
				var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneView");
				EditorWindow.GetWindow(type).Repaint();

				EditorApplication.delayCall += () =>
				{
					var t = GenerateCapturedTexture(SceneView.lastActiveSceneView.camera);
					var raw = t.EncodeToPNG();
					Texture.DestroyImmediate(t);
					File.WriteAllBytes(filePath, raw);
				};
				return;
			}

			var texture = GenerateCapturedTexture(SceneView.lastActiveSceneView.camera);
			var bytes = texture.EncodeToPNG();
			Texture.DestroyImmediate(texture);
			File.WriteAllBytes(filePath, bytes);
		}

		[MenuItem("jwellone/Capture/Take/Unity Editor %#u")]
		public static void TakeUnityEditor()
		{
			TakeUnityEditor(MakeFilePath("UnityEditor"));
		}

		public static void TakeUnityEditor(string filePath, Action<bool> callback = null)
		{
			TakePicture(GetMainWindowPosition(), filePath, callback);
		}

		public static Texture2D GenerateCapturedTexture(Camera targetCamera, TextureFormat format = TextureFormat.RGB24)
		{
			var temporaryRT = targetCamera.targetTexture == null ? RenderTexture.GetTemporary(Screen.width, Screen.height, 24, RenderTextureFormat.ARGBFloat) : null;
			if (temporaryRT)
			{
				targetCamera.targetTexture = temporaryRT;
			}

			var targetTexture = targetCamera.targetTexture;
			var texture = new Texture2D(targetTexture.width, targetTexture.height, format, false);
			var tmpRT = RenderTexture.active;

			RenderTexture.active = targetTexture;
			targetCamera.Render();
			texture.ReadPixels(new Rect(0, 0, targetTexture.width, targetTexture.height), 0, 0);

			RenderTexture.active = tmpRT;

			if (temporaryRT != null)
			{
				targetCamera.targetTexture = null;
				RenderTexture.ReleaseTemporary(temporaryRT);
			}

			return texture;
		}

		private static Rect GetMainWindowPosition()
		{
			var windowType = AppDomain.CurrentDomain
				.GetAssemblies()
				.SelectMany(a => a.GetTypes())
				.Where(t => t.IsSubclassOf(typeof(ScriptableObject)))
				.FirstOrDefault(t => t.Name == "ContainerWindow");

			if (windowType == null)
			{
				return Rect.zero;
			}

			var field = windowType.GetField("m_ShowMode", BindingFlags.NonPublic | BindingFlags.Instance);
			var property = windowType.GetProperty("position", BindingFlags.Public | BindingFlags.Instance);

			if (field == null || property == null)
			{
				return Rect.zero;
			}

			foreach (var window in Resources.FindObjectsOfTypeAll(windowType))
			{
				if ((int)field.GetValue(window) == 4)
				{
					return (Rect)property.GetValue(window, null);
				}
			}

			return Rect.zero;
		}

		private static void TakePicture(in Rect rect, string filePath, Action<bool> callback = null)
		{
			if (rect == Rect.zero)
			{
				callback?.Invoke(false);
				Debug.LogError($"[Capture]rect is zero. {filePath}");
				return;
			}

			try
			{
				var width = (int)rect.width;
				var height = (int)rect.height;
				var pixel = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(rect.position, width, height);
				var texture = new Texture2D(width, height, TextureFormat.RGB24, false, true);
				texture.SetPixels(pixel);

				var bytes = texture.EncodeToPNG();
				GameObject.DestroyImmediate(texture);

				File.WriteAllBytes(filePath, bytes);
				Debug.Log($"<color=#{ColorUtility.ToHtmlStringRGB(SuccessColor)}>[Capture]Succeed take picture. {filePath}</color>");
				callback?.Invoke(true);
			}
			catch (Exception ex)
			{
				callback?.Invoke(false);
				Debug.LogError($"[Capture]Failed take picture. {filePath} \n{ex}");
			}
		}

		private static string MakeUniquePath(string name)
		{
			var folder = Path.Combine(Application.dataPath, $"../ScreenShot");
			if (!Directory.Exists(folder))
			{
				Directory.CreateDirectory(folder);
			}

			var i = 0;
			var format = "{0}/{1}{2:0000}.png";
			var path = string.Empty;
			do
			{
				path = string.Format(format, folder, name, i++);
			}
			while (System.IO.File.Exists(path));

			return path;
		}
	}
}
