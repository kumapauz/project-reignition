using Godot;
using Project.Core;

/// <summary> Loops an audio stream seamlessly. </summary>
namespace Project;

public partial class BGMPlayer : AudioStreamPlayer
{
	[Export] private BGMResource bgmResource;
	public BGMResource GetBgmResource() => bgmResource;
	public void SetBgmResource(BGMResource resource)
	{
		Stop();
		bgmResource = resource;
	}

	[Export] public bool loadAsyncronously;

	private bool canLoop;
	private bool isFadingBgm;
	private float LoopLength => bgmResource.LoopEnd - bgmResource.LoopStart;

	public override void _EnterTree() => LoadBgmResource();

	public override void _Process(double _)
	{
		if (bgmResource == null)
			return;

		if (!Playing && Autoplay)
		{
			if (bgmResource != null && Mathf.IsEqualApprox(bgmResource.LoopEnd, -1))
				Play();

			return;
		}

		if (isFadingBgm && !SoundManager.FadeAudioPlayer(this, 0.5f))
			isFadingBgm = false;

		if (!canLoop) return;

		float currentPosition = GetPlaybackPosition() + (float)AudioServer.GetTimeSinceLastMix();
		if (!Mathf.IsEqualApprox(bgmResource.LoopEnd, -1) && currentPosition >= bgmResource.LoopEnd)
			Seek(currentPosition - LoopLength);
	}

	/// <summary> Updates the BgmPlayer's Stream. </summary>
	public void LoadBgmResource()
	{
		if (bgmResource == null)
			return;

		canLoop = bgmResource.LoopEnd > bgmResource.LoopStart || Mathf.IsEqualApprox(bgmResource.LoopEnd, -1.0f);
		if (!canLoop && bgmResource.LoopEnd <= bgmResource.LoopStart)
			GD.PrintErr("BGM loop points are set up incorrectly. Looping is disabled.");

		if (loadAsyncronously)
		{
			LoadBgmResourceAsync();
			return;
		}

		AudioStream stream = GetAudioStream();
		Stream = stream;

		if (Autoplay)
			Play();
	}

	private AudioStream GetAudioStream()
	{
		if (bgmResource.StreamPath.StartsWith("uid://"))
			return ResourceLoader.Load<AudioStream>(bgmResource.StreamPath);

		if (bgmResource.StreamPath.EndsWith(".wav"))
			return AudioStreamWav.LoadFromFile(bgmResource.StreamPath);

		if (bgmResource.StreamPath.EndsWith(".ogg"))
			return AudioStreamOggVorbis.LoadFromFile(bgmResource.StreamPath);

		if (bgmResource.StreamPath.EndsWith(".mp3"))
			return AudioStreamMP3.LoadFromFile(bgmResource.StreamPath);

		return null;
	}

	public async void LoadBgmResourceAsync()
	{
		if (ResourceLoader.LoadThreadedRequest(bgmResource.StreamPath) != Error.Ok)
			return; // Load failed

		while (ResourceLoader.LoadThreadedGetStatus(bgmResource.StreamPath) == ResourceLoader.ThreadLoadStatus.InProgress)
			await ToSignal(GetTree().CreateTimer(.1f), SceneTreeTimer.SignalName.Timeout); // Still loading; wait a bit

		Resource loadedResource = ResourceLoader.LoadThreadedGet(bgmResource.StreamPath);
		if (loadedResource is AudioStream)
			Stream = loadedResource as AudioStream;
		else
			GD.Print(loadedResource);

		if (Autoplay)
			Play();
	}

	public void RestartLoop()
	{
		if (GetPlaybackPosition() >= bgmResource.LoopEnd)
			Play(bgmResource.LoopStart);
	}

	public void Play()
	{
		if (bgmResource == null)
			return;

		VolumeDb = GetBgmResource().VolumeDB;
		isFadingBgm = false;
		Play(bgmResource.StartPosition);
	}

	public void QueueBgmFade() => isFadingBgm = true;
}