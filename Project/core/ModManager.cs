using Godot;
using System;
using System.Collections.Generic;
using Project.Gameplay;
using System.Linq;

namespace Project.Core;

public partial class ModManager : Node
{
	public static ModManager Instance;
	public readonly List<LevelDataResource> LevelMods = [];
	public readonly List<SkillResource> CharacterMods = [];

	// Mod paths
	private readonly string ResourceModPath = "res://mods/";
	private readonly string LevelPaths = "levels/";
	private readonly string CustomCharacterPaths = "characters/";
	private readonly string LanguagePaths = "lang/";
	private readonly string ExtrasPaths = "extras/";
	private readonly string PackExtension = "pck";
	private readonly string ZipExtension = "zip";
	private readonly string ResourceExtension = "tres";

	public override void _EnterTree() => Instance = this;

	public override void _Ready()
	{
		ExtractZipFiles();
		CallDeferred(MethodName.SetUpMods);
	}

	public void SetUpMods()
	{
		if (SaveManager.Config.areLevelModsEnabled)
			LoadLevelMods();
		if (SaveManager.Config.areCharaModsEnabled)
			LoadCharacterMods();
		if (SaveManager.Config.areLangModsEnabled)
			LoadLanguageMods();
		if (DirAccess.DirExistsAbsolute(SaveManager.ModDirectory + ExtrasPaths))
			LoadPcks(SaveManager.ModDirectory + ExtrasPaths);
	}

	/// <summary> Extracts all zip files, then deletes the original zip file. </summary>
	private void ExtractZipFiles()
	{
		LoadZips(SaveManager.ModDirectory);

		// Switch to local resource folder, now that zip are loaded
		DirAccess dirAccess = DirAccess.Open(ResourceModPath);
		if (DirAccess.GetOpenError() != Error.Ok)
			return;

		foreach (string folder in dirAccess.GetDirectories())
			LoadZips(ResourceModPath + folder + "/");
	}

	/// <summary> Loads a .pck from a directory. </summary>
	private void LoadPck(string file, string dir)
	{
		if (!file.GetExtension().Equals(PackExtension))
			return;

		if (!ProjectSettings.LoadResourcePack(dir + file))
			GD.PrintErr($"Couldn't load mod {dir + file}!");

		GD.Print($"Loaded PCK {dir + file}");
	}

	/// <summary> Loads pcks from a given directory. </summary>
	private void LoadPcks(string dir)
	{
		DirAccess dirAccess = DirAccess.Open(dir);
		foreach (string file in dirAccess.GetFiles())
			LoadPck(file, dir);
	}

	private void LoadZip(string file, string dir)
	{
		if (!file.GetExtension().Equals(ZipExtension))
			return;

		ZipReader reader = new();
		reader.Open(dir.PathJoin(file));

		// Extract the zip, copied directly from Godot Docs
		DirAccess rootDir = DirAccess.Open(dir);
		foreach (string filePath in reader.GetFiles())
		{
			if (filePath.EndsWith("/"))
			{
				rootDir.MakeDirRecursive(filePath);
				continue;
			}

			rootDir.MakeDirRecursive(rootDir.GetCurrentDir().PathJoin(filePath).GetBaseDir());
			FileAccess fileAccess = FileAccess.Open(rootDir.GetCurrentDir().PathJoin(filePath), FileAccess.ModeFlags.Write);
			byte[] buffer = reader.ReadFile(filePath);
			fileAccess.StoreBuffer(buffer);
		}

		reader.Close();
		OS.MoveToTrash(dir.PathJoin(file)); // Delete the original zip file
		GD.Print($"Extracted ZIP from {dir.PathJoin(file)} to {dir}");
	}

	/// <summary> Loads pcks from a given directory. </summary>
	private void LoadZips(string dir)
	{
		GD.Print($"Loading directory {dir}");
		DirAccess dirAccess = DirAccess.Open(dir);

		foreach (string folder in dirAccess.GetDirectories())
			LoadZips(dir.PathJoin(folder));

		foreach (string file in dirAccess.GetFiles())
			LoadZip(file, dir);
	}

	private void LoadLevelMods()
	{
		if (!DirAccess.DirExistsAbsolute(SaveManager.ModDirectory + LevelPaths))
			DirAccess.MakeDirRecursiveAbsolute(SaveManager.ModDirectory + LevelPaths);

		LoadPcks(SaveManager.ModDirectory + LevelPaths);

		// Switch to local resource folder, now that pcks are loaded
		DirAccess dirAccess = DirAccess.Open(ResourceModPath + LevelPaths);
		if (DirAccess.GetOpenError() != Error.Ok)
			return;

		foreach (string level in dirAccess.GetDirectories())
			LoadModLevel(ResourceModPath + LevelPaths + level + "/");
	}

	private void LoadModLevel(string dir)
	{
		DirAccess levelDir = DirAccess.Open(dir); // Access the specific mod directory
		string[] files = levelDir.GetFiles();
		foreach (string file in files) // Find the level data resource
		{
			string fileName = file;
			if (fileName.EndsWith(".remap"))
				fileName = fileName.Replace(".remap", string.Empty);

			if (!fileName.GetFile().GetExtension().Equals(ResourceExtension))
				continue;

			Resource resource = ResourceLoader.Load(dir + fileName);
			if (resource is not LevelDataResource)
				continue;

			LevelMods.Add(resource as LevelDataResource);
			GD.Print($"Loaded custom level {fileName}.");
		}
	}

	private void LoadCharacterMods()
	{
		if (!DirAccess.DirExistsAbsolute(SaveManager.ModDirectory + CustomCharacterPaths))
			DirAccess.MakeDirRecursiveAbsolute(SaveManager.ModDirectory + CustomCharacterPaths);

		LoadPcks(SaveManager.ModDirectory + CustomCharacterPaths);

		// Switch to local resource folder, now that pcks are loaded
		DirAccess dirAccess = DirAccess.Open(ResourceModPath + CustomCharacterPaths);
		if (DirAccess.GetOpenError() != Error.Ok)
			return;

		SkillResource baseCharacterSkill = Runtime.Instance.SkillList.GetSkill(SkillKey.Character);
		baseCharacterSkill.Augments = [];
		foreach (string character in dirAccess.GetDirectories())
			LoadModCharacter(ResourceModPath + CustomCharacterPaths + character + "/", baseCharacterSkill);
	}

	private void LoadModCharacter(string dir, SkillResource baseCharacterSkill)
	{
		DirAccess levelDir = DirAccess.Open(dir); // Access the specific mod directory
		string[] files = levelDir.GetFiles();
		foreach (string file in files) // Find the level data resource
		{
			string fileName = file;
			if (fileName.EndsWith(".remap"))
				fileName = fileName.Replace(".remap", string.Empty);

			if (!fileName.GetFile().GetExtension().Equals(ResourceExtension))
				continue;

			Resource resource = ResourceLoader.Load(dir + fileName);
			if (resource is not SkillResource)
				continue;

			SkillResource characterResource = resource.Duplicate() as SkillResource;
			characterResource.Key = SkillKey.Character;
			characterResource.Element = SkillResource.SkillElement.Config;
			characterResource.Category = SkillResource.SkillCategory.Setting;
			characterResource.AugmentIndex = baseCharacterSkill.Augments.Count + 1;
			baseCharacterSkill.Augments.Add(characterResource);
			CharacterMods.Add(characterResource);
			GD.Print($"Loaded custom character {fileName} in slot {characterResource.AugmentIndex}");
		}
	}

	private void LoadModLanguage(string dir)
	{
		DirAccess levelDir = DirAccess.Open(dir); // Access the specific mod directory
		string[] files = levelDir.GetFiles();
		foreach (string file in files) // Load language resources
		{
			string fileName = file;
			if (fileName.EndsWith(".remap"))
				fileName = fileName.Replace(".remap", string.Empty);

			if (!fileName.GetFile().GetExtension().Equals(ResourceExtension))
				continue;

			Resource resource = ResourceLoader.Load(dir + fileName);
			if (resource is not LocalizationResource)
				continue;

			GD.Print($"Found localization resource {fileName}.");
			LocalizationResource locale = resource as LocalizationResource;
			locale.IsMod = true;
			if (locale.LocaleType == LocalizationResource.LocalizationType.Text)
			{
				if (SaveManager.FindTextLocaleIndex(locale.LocaleId) != -1) // Already exists
					continue;

				string resourcePath = $"res://locale/Locale.{locale.LocaleId}.translation";

				if (!ResourceLoader.Exists(resourcePath))
				{
					GD.PrintErr($"Can't find optimized translation resource {resourcePath}, text localization {locale.LocaleId} won't be loaded");
					continue;
				}

				OptimizedTranslation translation = (OptimizedTranslation)ResourceLoader.Load(resourcePath);
				TranslationServer.AddTranslation(translation);

				SaveManager.Instance.TextLocalizations.Add(locale);
			}
			else
			{
				if (SaveManager.FindVoiceLocaleIndex(locale.LocaleId) != -1) // Already exists
					continue;

				SaveManager.Instance.VoiceLocalizations.Add(locale);
			}

			GD.Print($"Loaded custom language {fileName}.");
		}
	}

	private void LoadLanguageMods()
	{
		if (!DirAccess.DirExistsAbsolute(SaveManager.ModDirectory + LanguagePaths))
			DirAccess.MakeDirRecursiveAbsolute(SaveManager.ModDirectory + LanguagePaths);

		LoadPcks(SaveManager.ModDirectory + LanguagePaths);

		// Switch to local resource folder, now that pcks are loaded
		DirAccess dirAccess = DirAccess.Open(ResourceModPath + LanguagePaths);
		if (DirAccess.GetOpenError() != Error.Ok)
			return;

		LoadModLanguage(ResourceModPath + LanguagePaths + "/"); // Load base language folder
		foreach (string language in dirAccess.GetDirectories())
			LoadModLanguage(ResourceModPath + LanguagePaths + language + "/"); // Load nested language folders

		SaveManager.LoadConfig(); // Reload the config in case a mod language was originally selected
	}
}
