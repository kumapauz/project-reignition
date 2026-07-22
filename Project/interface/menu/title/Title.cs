using Godot;
using Project.Core;

namespace Project.Interface.Menus
{
	// / <summary>
	// / Press start. Also plays an intro cutscene if you wait long enough.
	// / </summary>
	public partial class Title : Menu
	{
		[Export] private Label versionLabel;
		[Export] private Control startTextSprite;

		private bool isCutsceneActive;
		private float cutsceneTimer;
		private const float CUTSCENE_TIME_LENGTH = 20f;

		protected override void SetUp()
		{
			isProcessing = menuMemory[MemoryKeys.ActiveMenu] == (int)MemoryKeys.Title;
			versionLabel.Text = $"Version {(string)ProjectSettings.GetSetting("application/config/version")}";

			OnHUDToggled();
			DebugManager.Instance.Connect(DebugManager.SignalName.HUDToggled, new Callable(this, MethodName.OnHUDToggled));

			base.SetUp();
		}

		private void OnHUDToggled()
		{
			versionLabel.Visible = !DebugManager.Instance.DisableHUD;
			startTextSprite.Visible = !DebugManager.Instance.DisableHUD;
		}

		protected override void ProcessMenu()
		{
			if (isCutsceneActive)
			{
				if ((Runtime.Instance.IsActionJustPressed("sys_pause", "ui_accept") && !Input.IsActionJustPressed("toggle_fullscreen")) ||
					Runtime.Instance.IsActionJustPressed("sys_select", "ui_select"))
					FinishCutscene();

				return;
			}

			if (Input.IsAnythingPressed()) // Change menu
			{
				Runtime.Instance.IsUsingMouse = Input.IsActionJustPressed("mouse_left");
				Confirm();
				return;
			}

			cutsceneTimer += PhysicsManager.physicsDelta;
			if (cutsceneTimer >= CUTSCENE_TIME_LENGTH && !isCutsceneActive)
			{
				StartCutscene();
				return;
			}
		}

		// Activate main menu (submenu 0);
		public override void OpenSubmenu() => _submenus[0].ShowMenu();

		public override void ShowMenu()
		{
			animator.Play("RESET");
			animator.Seek(0, true);

			if (SaveManager.Config.useProjectReignitionBranding)
			{
				animator.Play("pr-logo");
				animator.Advance(0.0);
			}

			animator.Play(ShowAnimation);

			cutsceneTimer = 0;
		}

		private void StartCutscene()
		{
			isCutsceneActive = true;
			animator.Play("cutscene-start");
			DisableProcessing();
		}

		private void FinishCutscene()
		{
			cutsceneTimer = 0;
			isCutsceneActive = false;
			animator.Play("cutscene-finish");
			DisableProcessing();
		}
	}
}
