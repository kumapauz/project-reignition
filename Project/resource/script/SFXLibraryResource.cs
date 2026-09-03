using Godot;
using Godot.Collections;
using Project.Core;

namespace Project.Gameplay;

/// <summary>
/// A collection of sound effects. Each key can have multiple sound effects associated with it, which will be chosen randomly.
/// Note: There isn't any restrictions to avoid having the same sound effect play multiple times in a row.
/// Important note: DO NOT reorder keys, otherwise stream data will become desynced
/// </summary>
[Tool]
[GlobalClass]
public partial class SFXLibraryResource : Resource
{
	#region Editor
	public override Array<Dictionary> _GetPropertyList()
	{
		Array<Dictionary> properties = [];
		ValidateArrays();

		channelEditingIndex = Mathf.Clamp(channelEditingIndex, 1, channelCount);
		keyEditingIndex = Mathf.Clamp(keyEditingIndex, 0, KeyCount);

		properties.Add(ExtensionMethods.CreateProperty("Editing/Organization/Target", Variant.Type.Int, PropertyHint.Enum, GetKeyList(keys)));
		properties.Add(ExtensionMethods.CreateProperty("Editing/Organization/Mode", Variant.Type.Int, PropertyHint.Enum, reorderMode.EnumToString()));
		properties.Add(ExtensionMethods.CreateProperty("Editing/Organization/Reorder", Variant.Type.Bool));
		properties.Add(ExtensionMethods.CreateProperty("Editing/Key Name", Variant.Type.Int, PropertyHint.Enum, GetKeyList(keys)));
		properties.Add(ExtensionMethods.CreateProperty("Editing/Key Index", Variant.Type.Int, PropertyHint.Range, $"0,{keys.Count - 1}"));
		properties.Add(ExtensionMethods.CreateProperty("Editing/Channel", Variant.Type.Int, PropertyHint.Range, "1, 9"));

		if (KeyCount != 0)
			properties.Add(ExtensionMethods.CreateProperty("Editing/Streams", Variant.Type.Array, PropertyHint.TypeString, "24/17:AudioStream"));

		return properties;
	}

	public override Variant _Get(StringName property)
	{
		switch ((string)property)
		{
			case "Editing/Organization/Target":
				return reorderIndex;
			case "Editing/Organization/Mode":
				return (int)reorderMode;
			case "Editing/Key Name":
				return keyEditingIndex;
			case "Editing/Key Index":
				return keyEditingIndex;
			case "Editing/Channel":
				return channelEditingIndex;
			case "Editing/Streams":
				if (isLocalizedVoiceLines)
					return GetLocalizationStreams(channelEditingIndex - 1, keyEditingIndex);

				return streams[channelEditingIndex - 1][keyEditingIndex];
		}

		return base._Get(property);
	}

	public override bool _Set(StringName property, Variant value)
	{
		switch ((string)property)
		{
			case "Editing/Organization/Target":
				reorderIndex = (int)value;
				break;
			case "Editing/Organization/Mode":
				reorderMode = (ReorderModeEnum)(int)value;
				break;
			case "Editing/Organization/Reorder":
				if ((bool)value)
					ReorderKey();
				NotifyPropertyListChanged();
				break;
			case "Editing/Key Name":
				keyEditingIndex = (int)value;
				NotifyPropertyListChanged();
				break;
			case "Editing/Key Index":
				keyEditingIndex = (int)value;
				NotifyPropertyListChanged();
				break;
			case "Editing/Channel":
				channelEditingIndex = (int)value;
				NotifyPropertyListChanged();
				break;
			case "Editing/Streams":
				if (isLocalizedVoiceLines)
				{
					localizedStreamPaths[channelEditingIndex - 1][keyEditingIndex] = SetLocalizationStreams((Array<AudioStream>)value);
					break;
				}

				streams[channelEditingIndex - 1][keyEditingIndex] = (Array<AudioStream>)value;
				break;
			default:
				return false;
		}

		return true;
	}

	private Array<AudioStream> GetLocalizationStreams(int channelIndex, int keyIndex)
	{
		Array<AudioStream> returnArr = [];
		for (int i = 0; i < localizedStreamPaths[channelIndex][keyIndex].Count; i++)
		{
			string targetFile = localizedStreamPaths[channelIndex][keyIndex][i];

			if (!ResourceLoader.Exists(targetFile))
				returnArr.Add(null);
			else
				returnArr.Add(ResourceLoader.Load<AudioStream>(targetFile));
		}

		return returnArr;
	}

	private Array<string> SetLocalizationStreams(Array<AudioStream> streams)
	{
		Array<string> returnArr = [];
		for (int i = 0; i < streams.Count; i++)
		{
			if (streams[i] == null)
				returnArr.Add(string.Empty);
			else
				returnArr.Add(streams[i].ResourcePath);
		}

		return returnArr;
	}

	private void ReorderKey()
	{
		if (!Engine.IsEditorHint())
			return;

		if (reorderIndex == keyEditingIndex)
		{
			GD.Print("Target key and editing key are the same. Nothing will happen.");
			return;
		}

		if (isLocalizedVoiceLines)
			ReorderLocalizationKey();
		else
			ReorderAudioStream();
	}

	private void ReorderLocalizationKey()
	{
		string key = keys[keyEditingIndex];
		Array<Array<string>> stream = [];
		GD.PrintT(keys.Count, keyEditingIndex);
		for (int i = 0; i < channelCount; i++)
			stream.Add(localizedStreamPaths[i][keyEditingIndex]);

		if (reorderMode == ReorderModeEnum.Swap)
		{
			keys[keyEditingIndex] = keys[reorderIndex];
			for (int i = 0; i < channelCount; i++)
				localizedStreamPaths[i][keyEditingIndex] = localizedStreamPaths[i][reorderIndex];

			keys[reorderIndex] = key;
			for (int i = 0; i < channelCount; i++)
				localizedStreamPaths[i][reorderIndex] = stream[i];

			keyEditingIndex = reorderIndex;
			GD.Print($"Swapped positions of {keys[reorderIndex]} and {keys[keyEditingIndex]}.");
			return;
		}

		keys.RemoveAt(keyEditingIndex); // Remove the data at the current index
		for (int i = 0; i < channelCount; i++)
			localizedStreamPaths[i].RemoveAt(keyEditingIndex);

		int insertionPoint = reorderIndex;
		if (reorderMode == ReorderModeEnum.After)
			insertionPoint = reorderIndex + 1;
		if (keyEditingIndex < reorderIndex) // Take deletions into account
			insertionPoint--;

		if (insertionPoint >= KeyCount)
		{
			keys.Add(key);
			for (int i = 0; i < channelCount; i++)
				localizedStreamPaths[i].Add(stream[i]);
		}
		else
		{
			keys.Insert(insertionPoint, key);
			for (int i = 0; i < channelCount; i++)
				localizedStreamPaths[i].Insert(insertionPoint, stream[i]);
		}

		keyEditingIndex = insertionPoint;
		GD.Print($"Moved {key}.");
	}

	private void ReorderAudioStream()
	{
		string key = keys[keyEditingIndex];
		Array<Array<AudioStream>> stream = [];
		for (int i = 0; i < channelCount; i++)
			stream.Add(streams[i][keyEditingIndex]);

		if (reorderMode == ReorderModeEnum.Swap)
		{
			keys[keyEditingIndex] = keys[reorderIndex];
			for (int i = 0; i < channelCount; i++)
				streams[i][keyEditingIndex] = streams[i][reorderIndex];

			keys[reorderIndex] = key;
			for (int i = 0; i < channelCount; i++)
				streams[i][reorderIndex] = stream[i];

			keyEditingIndex = reorderIndex;
			GD.Print($"Swapped positions of {keys[reorderIndex]} and {keys[keyEditingIndex]}.");
			return;
		}

		keys.RemoveAt(keyEditingIndex); // Remove the data at the current index
		for (int i = 0; i < channelCount; i++)
			streams[i].RemoveAt(keyEditingIndex);

		int insertionPoint = reorderIndex;
		if (reorderMode == ReorderModeEnum.After)
			insertionPoint = reorderIndex + 1;
		if (keyEditingIndex < reorderIndex) // Take deletions into account
			insertionPoint--;

		if (insertionPoint >= KeyCount)
		{
			keys.Add(key);
			for (int i = 0; i < channelCount; i++)
				streams[i].Add(stream[i]);
		}
		else
		{
			keys.Insert(insertionPoint, key);
			for (int i = 0; i < channelCount; i++)
				streams[i].Insert(insertionPoint, stream[i]);
		}

		keyEditingIndex = insertionPoint;
		GD.Print($"Moved {key}.");
	}

	private string GetKeyList(Array<StringName> array)
	{
		string value = string.Empty;
		for (int i = 0; i < array.Count; i++)
		{
			value += array[i];
			if (i < array.Count - 1)
				value += ",";
		}

		return value;
	}

	/// <summary>
	/// Ensures all arrays are the correct size and are not null.
	/// </summary>
	private void ValidateArrays()
	{
		keys ??= [];

		if (isLocalizedVoiceLines)
		{
			localizedStreamPaths ??= [];

			if (localizedStreamPaths.Count != channelCount)
				localizedStreamPaths.Resize(channelCount);

			for (int i = 0; i < channelCount; i++)
			{
				if (localizedStreamPaths[i] == null || localizedStreamPaths[i].Count == 0)
					localizedStreamPaths[i] = [];

				if (localizedStreamPaths[i].Count != KeyCount)
					localizedStreamPaths[i].Resize(KeyCount);

				for (int j = 0; j < KeyCount; j++)
				{
					if (localizedStreamPaths[i][j] == null || localizedStreamPaths[i][j].Count == 0)
						localizedStreamPaths[i][j] = [];
				}
			}
		}
		else
		{
			streams ??= [];

			if (streams.Count != channelCount)
				streams.Resize(channelCount);

			for (int i = 0; i < channelCount; i++)
			{
				if (streams[i] == null || streams[i].Count == 0)
					streams[i] = [];

				if (streams[i].Count != KeyCount)
					streams[i].Resize(KeyCount);

				for (int j = 0; j < KeyCount; j++)
				{
					if (streams[i][j] == null || streams[i][j].Count == 0)
						streams[i][j] = [];
				}
			}
		}
	}

	/// <summary>
	/// Ensure there aren't any duplicate keys.
	/// </summary>
	private void CheckDuplicateKeys()
	{
		Array<string> duplicateKeyChecker = [];
		for (int i = 0; i < KeyCount; i++)
		{
			if (string.IsNullOrEmpty(keys[i]))
				GD.PushWarning($"Voice Key '{i}' is empty.");
			else if (duplicateKeyChecker.Contains(keys[i]))
				GD.PushWarning($"Voice Key '{keys[i]}' (Index {i}) is a duplicate.");
			else
				duplicateKeyChecker.Add(keys[i]);
		}
		duplicateKeyChecker.Clear();
	}
	#endregion

	[ExportToolButton("Refresh Resource")]
	public Callable RefreshResourceGroup => Callable.From(NotifyPropertyListChanged);
	[ExportToolButton("Auto-setup Localization Audio")]
	public Callable SetUpLocalizationGroup => Callable.From(() => LocalizeAudioStreams(false));
	[Export] private string rootAudioPath;
	[Export] private SFXLibraryResource fallbackResource;
	[Export] private Array<StringName> keys;
	public int KeyCount => keys.Count;
	/// <summary> Arrays are ordered in Channel -> Key -> Index. </summary>
	[Export] private Array<Array<Array<AudioStream>>> streams;
	/// <summary> Used for localized audio, that way we don't have to load excess audio streams for no reason. </summary>
	[Export] private Array<Array<Array<string>>> localizedStreamPaths;
	[Export] private bool isLocalizedVoiceLines;

	/// <summary>
	/// How many channels does this library contain?
	/// Voice libraries should have 3. [0 -> En, 1 -> Ja, 2 -> Es]
	/// </summary>
	[Export] private int channelCount = 1;

	/// <summary> Current channel index being edited in the inspector. </summary>
	private int channelEditingIndex;
	/// <summary> Current key index being edited in the inspector. </summary>
	private int keyEditingIndex;

	private int reorderIndex;
	private ReorderModeEnum reorderMode;
	/// <summary> Reorder swap mode. </summary>
	private enum ReorderModeEnum
	{
		Before, // Moves the current key before the target key
		After, // Moves the current key after the target key
		Swap, // Swaps two keys with each other
	}

	private readonly string[] BuiltInLocales = ["en", "ja"];

	/// <summary> Automatically set up localized audio streams. Do NOT use this for unlocalized sound effects. </summary>
	public void LocalizeAudioStreams(bool recursive = false)
	{
		if (recursive && fallbackResource != null)
			fallbackResource.LocalizeAudioStreams(true);

		if (!isLocalizedVoiceLines)
		{
			GD.PrintErr("Given resource is not configured as a localizable audio pack.");
			return;
		}

		AutoDetectEnglishClips();

		if (Engine.IsEditorHint())
			channelCount = BuiltInLocales.Length;
		else
		{
			channelCount = SaveManager.Instance.VoiceLocalizations.Count;
			GD.Print(channelCount);
		}

		Array<Array<Array<string>>> tempStreamPaths = [];

		if (streams == null || streams.Count == 0)
		{
			// Keep existing tracks
			tempStreamPaths.Add(localizedStreamPaths[0]);
		}
		else
		{
			// Convert from audio streams to file paths
			tempStreamPaths.Add([]);

			for (int i = 0; i < streams[0].Count; i++)
			{
				tempStreamPaths[0].Add([]); // Add a dialog slot

				for (int j = 0; j < streams[0][i].Count; j++)
				{
					// Copy audio file paths
					if (streams[0][i][j] == null)
					{
						tempStreamPaths[0][i].Add(string.Empty);
						continue;
					}

					string targetAudioFile = streams[0][i][j].ResourcePath;
					tempStreamPaths[0][i].Add(targetAudioFile);
				}
			}
		}

		string[] locales = new string[channelCount];
		if (Engine.IsEditorHint())
		{
			locales = BuiltInLocales;
		}
		else
		{
			for (int i = 0; i < SaveManager.Instance.VoiceLocalizations.Count; i++)
				locales[i] = SaveManager.Instance.VoiceLocalizations[i].LocaleId;
		}

		for (int i = 1; i < locales.Length; i++)
		{
			tempStreamPaths.Add([]); // Add a language slot
			string lang = locales[i];

			for (int j = 0; j < tempStreamPaths[0].Count; j++)
			{
				tempStreamPaths[i].Add([]); // Add a dialog slot

				for (int k = 0; k < tempStreamPaths[0][j].Count; k++)
				{
					// Add the actual audio files
					string targetAudioFile = tempStreamPaths[0][j][k];
					targetAudioFile = targetAudioFile.Replace("/en/", $"/{lang}/");
					tempStreamPaths[i][j].Add(targetAudioFile);
				}
			}
		}

		// Clear streams to prevent loading audio streams when loading the resource.
		streams = null;
		localizedStreamPaths = tempStreamPaths;
		NotifyPropertyListChanged();

		if (Engine.IsEditorHint())
			ResourceSaver.Save(this);
	}

	private void AutoDetectEnglishClips()
	{
		if (!rootAudioPath.IsAbsolutePath())
		{
			GD.Print($"rootAudioPath is not configured properly. File paths must be configured manually.");
			return;
		}

		DirAccess dir = DirAccess.Open(rootAudioPath + "en/");
		if (DirAccess.GetOpenError() != Error.Ok)
			GD.PrintErr($"Couldn't open {rootAudioPath}. Error {DirAccess.GetOpenError()}.");

		string[] files = dir.GetFiles();
		for (int i = 0; i < keys.Count; i++)
		{
			Array<string> filePaths = [];
			foreach (string file in files)
			{
				if (file.StartsWith(keys[i]) && (file.EndsWith(".wav") || file.EndsWith(".ogg") || file.EndsWith(".mp3")))
					filePaths.Add(rootAudioPath + "en/" + file);
			}

			if (filePaths.Count != 0)
				localizedStreamPaths[0][i] = filePaths;
		}
	}

	/// <summary>
	/// Returns a random sound effect from a library.
	/// Channel can be useful for multiple languages.
	/// sfxIndex can be used to override rng.
	/// </summary>
	public AudioStream GetStream(StringName key, int channel = 0, int sfxIndex = -1)
	{
		int keyIndex = keys.GetStringNameIndex(key);

		if (keyIndex == -1)
		{
			if (fallbackResource != null) // Fallback if possible
				return fallbackResource.GetStream(key, channel, sfxIndex);

			GD.PushWarning($"Couldn't find sfx '{key}'!");
			return null;
		}

		// Get max random index
		int maxIndex = GetMaxIndex(channel, keyIndex);
		if (maxIndex == 0) // No sound effect found
		{
			if (fallbackResource != null) // Fallback if possible
				return fallbackResource.GetStream(key, channel, sfxIndex);

			GD.PrintErr($"No sfx found for '{key}' on channel {channel}!");
			return null;
		}

		if (maxIndex == 1) // Randomization isn't possible with only one sfx.
			sfxIndex = 0;
		else if (sfxIndex == -1) // Randomize sfx
			sfxIndex = Runtime.randomNumberGenerator.RandiRange(0, maxIndex - 1);

		if (isLocalizedVoiceLines)
		{
			string targetFile = localizedStreamPaths[channel][keyIndex][sfxIndex];
			if (string.IsNullOrEmpty(targetFile)) // No dialog clip
				return null;

			if (!ResourceLoader.Exists(targetFile))
			{
				GD.PushError($"{targetFile} doesn't exist!");
				return null;
			}

			return ResourceLoader.Load<AudioStream>(targetFile);
		}

		return streams[channel][keyIndex][sfxIndex];
	}

	private int GetMaxIndex(int channel, int keyIndex)
	{
		if (isLocalizedVoiceLines)
		{
			if (localizedStreamPaths.Count <= channel)
				return 0;

			if (localizedStreamPaths[channel].Count <= keyIndex)
				return 0;

			return localizedStreamPaths[channel][keyIndex].Count;
		}
		else
		{
			if (streams.Count <= channel)
				return 0;

			if (streams[channel].Count <= keyIndex)
				return 0;

			return streams[channel][keyIndex].Count;
		}
	}

	public AudioStream GetStream(int index, int channel = 0, int sfxIndex = -1) => GetStream(GetKeyByIndex(index), channel, sfxIndex);
	public StringName GetKeyByIndex(int index) => keys[index];

	/// <summary> Returns a Dialog audio clip, but fallback to English if it's not found. </summary>
	public AudioStream GetDialogStream(StringName key, int channel)
	{
		int keyIndex = keys.GetStringNameIndex(key);
		if (keyIndex == -1)
		{
			if (fallbackResource != null) // Fallback if possible
				return fallbackResource.GetDialogStream(key, channel);

			GD.PushWarning($"Couldn't find dialog stream '{key}'!");
			return null;
		}

		if (channel > channelCount - 1) // Fallback to English
			channel = 0;

		if (channel != 0)
		{
			if (isLocalizedVoiceLines && localizedStreamPaths[channel][keyIndex].Count != 0)
				return GetStream(key, channel);

			if (!isLocalizedVoiceLines && streams[channel][keyIndex].Count != 0)
				return GetStream(key, channel);
		}

		return GetStream(key, 0); // English fallback
	}

	/// <summary> Wrapper function for party mode gdscripts. </summary>
	public int CurrentLanguageIndex => SaveManager.GetCurrentVoiceLocaleIndex();
}
