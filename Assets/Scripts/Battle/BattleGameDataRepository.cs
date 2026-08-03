using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Protocol;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Battle
{
	public sealed class BattleGameDataRepository
	{
		const string ClassKeyTable = "ClassKey";
		const string PawnTemplateTable = "PawnTemplate";
		const string BattleSkillTable = "BattleSkill";
		const string BattleSkillEffectTable = "BattleSkillEffect";
		const string BattleSkillEffectParamTable = "BattleSkillEffectParam";
		const string BattleSkillViewTable = "BattleSkillView";
		const string BattleZocTable = "BattleZoc";
		const string DisplayTextTable = "DisplayText";
		const string EnumDefTable = "EnumDef";

		static readonly TableAsset[] Tables =
		{
			new TableAsset(ClassKeyTable, "Assets/GameData/ClassKey.csv"),
			new TableAsset(PawnTemplateTable, "Assets/GameData/PawnTemplate.csv"),
			new TableAsset(BattleSkillTable, "Assets/GameData/BattleSkill.csv"),
			new TableAsset(BattleSkillEffectTable, "Assets/GameData/BattleSkillEffect.csv"),
			new TableAsset(BattleSkillEffectParamTable, "Assets/GameData/BattleSkillEffectParam.csv"),
			new TableAsset(BattleSkillViewTable, "Assets/GameData/BattleSkillView.csv"),
			new TableAsset(BattleZocTable, "Assets/GameData/BattleZoc.csv"),
			new TableAsset(DisplayTextTable, "Assets/GameData/DisplayText.csv"),
			new TableAsset(EnumDefTable, "Assets/GameData/EnumDef.csv"),
		};

		readonly Dictionary<string, PawnClass> _classKeyToPawnClass = new Dictionary<string, PawnClass>(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<PawnClass, string> _pawnClassToClassKey = new Dictionary<PawnClass, string>();
		readonly Dictionary<string, BattlePawnTemplateDefinition> _pawnTemplatesByClassKey = new Dictionary<string, BattlePawnTemplateDefinition>(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<PawnClass, BattlePawnTemplateDefinition> _pawnTemplatesByPawnClass = new Dictionary<PawnClass, BattlePawnTemplateDefinition>();
		readonly Dictionary<string, BattleSkillDefinition> _skillsByKey = new Dictionary<string, BattleSkillDefinition>(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<string, BattleSkillDefinition> _skillsByClassSlot = new Dictionary<string, BattleSkillDefinition>(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<string, List<BattleSkillEffectDefinition>> _effectsByGroup = new Dictionary<string, List<BattleSkillEffectDefinition>>(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<string, Dictionary<string, string>> _effectParamsByInstance = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<string, BattleSkillViewDefinition> _skillViewsBySkillKey = new Dictionary<string, BattleSkillViewDefinition>(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<PawnClass, BattleZocDefinition> _zocProfilesByPawnClass = new Dictionary<PawnClass, BattleZocDefinition>();
		readonly Dictionary<string, BattleDisplayTextSet> _displayTexts = new Dictionary<string, BattleDisplayTextSet>(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<string, List<BattleEnumDefinition>> _enumDefinitions = new Dictionary<string, List<BattleEnumDefinition>>(StringComparer.OrdinalIgnoreCase);

		public IReadOnlyDictionary<string, PawnClass> ClassKeyToPawnClass => _classKeyToPawnClass;
		public IReadOnlyDictionary<string, BattlePawnTemplateDefinition> PawnTemplatesByClassKey => _pawnTemplatesByClassKey;
		public IReadOnlyDictionary<string, BattleSkillDefinition> SkillsByKey => _skillsByKey;
		public IReadOnlyDictionary<string, BattleSkillViewDefinition> SkillViewsBySkillKey => _skillViewsBySkillKey;
		public IReadOnlyDictionary<PawnClass, BattleZocDefinition> ZocProfilesByPawnClass => _zocProfilesByPawnClass;

		public static async Task<BattleGameDataRepository> LoadAsync()
		{
			Dictionary<string, string> csvByTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (TableAsset table in Tables)
			{
				string text = await LoadCsvTextAsync(table);
				if (string.IsNullOrWhiteSpace(text))
				{
					Debug.LogWarning($"GameData table is empty or missing: {table.Address}");
					continue;
				}

				csvByTable[table.Address] = text;
			}

			BattleGameDataRepository repository = new BattleGameDataRepository();
			repository.Parse(csvByTable);
			return repository;
		}

		public bool TryGetPawnClass(string classKey, out PawnClass pawnClass)
		{
			return _classKeyToPawnClass.TryGetValue(classKey, out pawnClass);
		}

		public bool TryGetClassKey(PawnClass pawnClass, out string classKey)
		{
			return _pawnClassToClassKey.TryGetValue(pawnClass, out classKey);
		}

		public bool TryGetPawnTemplate(string classKey, out BattlePawnTemplateDefinition template)
		{
			return _pawnTemplatesByClassKey.TryGetValue(classKey, out template);
		}

		public bool TryGetPawnTemplate(PawnClass pawnClass, out BattlePawnTemplateDefinition template)
		{
			return _pawnTemplatesByPawnClass.TryGetValue(pawnClass, out template);
		}

		public bool TryGetSkill(string skillKey, out BattleSkillDefinition skill)
		{
			return _skillsByKey.TryGetValue(skillKey, out skill);
		}

		public bool TryGetSkill(string classKey, int actionSlot, out BattleSkillDefinition skill)
		{
			return _skillsByClassSlot.TryGetValue(BuildClassSlotKey(classKey, actionSlot), out skill);
		}

		public bool TryGetSkill(PawnClass pawnClass, int actionSlot, out BattleSkillDefinition skill)
		{
			skill = null;
			return TryGetClassKey(pawnClass, out string classKey)
				&& TryGetSkill(classKey, actionSlot, out skill);
		}

		public IReadOnlyList<BattleSkillEffectDefinition> GetEffects(string effectGroupKey)
		{
			return _effectsByGroup.TryGetValue(effectGroupKey, out List<BattleSkillEffectDefinition> effects)
				? effects
				: Array.Empty<BattleSkillEffectDefinition>();
		}

		public IReadOnlyDictionary<string, string> GetEffectParams(string effectGroupKey, string effectInstanceKey)
		{
			return _effectParamsByInstance.TryGetValue(BuildEffectInstanceKey(effectGroupKey, effectInstanceKey), out Dictionary<string, string> parameters)
				? parameters
				: EmptyStringDictionary.Instance;
		}

		public bool TryGetSkillView(string skillKey, out BattleSkillViewDefinition view)
		{
			return _skillViewsBySkillKey.TryGetValue(skillKey, out view);
		}

		public bool TryGetZocProfile(PawnClass pawnClass, out BattleZocDefinition profile)
		{
			return _zocProfilesByPawnClass.TryGetValue(pawnClass, out profile);
		}

		public bool TryGetDisplayText(string ownerType, string ownerKey, out BattleDisplayTextSet textSet)
		{
			return _displayTexts.TryGetValue(BuildDisplayTextKey(ownerType, ownerKey), out textSet);
		}

		public IReadOnlyList<BattleEnumDefinition> GetEnumDefinitions(string enumName)
		{
			return _enumDefinitions.TryGetValue(enumName, out List<BattleEnumDefinition> definitions)
				? definitions
				: Array.Empty<BattleEnumDefinition>();
		}

		void Parse(Dictionary<string, string> csvByTable)
		{
			if (csvByTable.TryGetValue(ClassKeyTable, out string classKeyCsv))
				ParseClassKey(classKeyCsv);

			if (csvByTable.TryGetValue(PawnTemplateTable, out string pawnTemplateCsv))
				ParsePawnTemplate(pawnTemplateCsv);

			if (csvByTable.TryGetValue(BattleSkillTable, out string battleSkillCsv))
				ParseBattleSkill(battleSkillCsv);

			if (csvByTable.TryGetValue(BattleSkillEffectTable, out string battleSkillEffectCsv))
				ParseBattleSkillEffect(battleSkillEffectCsv);

			if (csvByTable.TryGetValue(BattleSkillEffectParamTable, out string battleSkillEffectParamCsv))
				ParseBattleSkillEffectParam(battleSkillEffectParamCsv);

			if (csvByTable.TryGetValue(BattleSkillViewTable, out string battleSkillViewCsv))
				ParseBattleSkillView(battleSkillViewCsv);

			if (csvByTable.TryGetValue(BattleZocTable, out string battleZocCsv))
				ParseBattleZoc(battleZocCsv);

			if (csvByTable.TryGetValue(DisplayTextTable, out string displayTextCsv))
				ParseDisplayText(displayTextCsv);

			if (csvByTable.TryGetValue(EnumDefTable, out string enumDefCsv))
				ParseEnumDef(enumDefCsv);

			foreach (List<BattleSkillEffectDefinition> effects in _effectsByGroup.Values)
				effects.Sort((left, right) => left.EffectOrder.CompareTo(right.EffectOrder));
		}

		void ParseClassKey(string csv)
		{
			foreach (CsvRow row in ReadRows(csv))
			{
				string classKey = row.Get("ClassKey");
				if (string.IsNullOrWhiteSpace(classKey))
					continue;

				if (TryParsePawnClass(row.Get("PawnClass"), out PawnClass pawnClass) == false)
				{
					Debug.LogWarning($"Invalid PawnClass in ClassKey.csv. classKey={classKey}, value={row.Get("PawnClass")}");
					continue;
				}

				_classKeyToPawnClass[classKey] = pawnClass;
				_pawnClassToClassKey[pawnClass] = classKey;
			}
		}

		void ParsePawnTemplate(string csv)
		{
			foreach (CsvRow row in ReadRows(csv))
			{
				string classKey = row.Get("ClassKey");
				if (string.IsNullOrWhiteSpace(classKey))
					continue;

				BattlePawnTemplateDefinition template = new BattlePawnTemplateDefinition(
					classKey,
					_classKeyToPawnClass.TryGetValue(classKey, out PawnClass pawnClass) ? pawnClass : PawnClass.None,
					ParseRole(row.Get("Role")),
					row.GetInt("BaseStr"),
					row.GetInt("BaseCon"),
					row.GetInt("BaseDex"),
					row.GetInt("BaseSpell"),
					row.GetInt("BaseDefense"),
					row.GetInt("BaseFocus"),
					row.GetInt("BaseWill"));

				_pawnTemplatesByClassKey[classKey] = template;
				if (template.PawnClass != PawnClass.None)
					_pawnTemplatesByPawnClass[template.PawnClass] = template;
			}
		}

		void ParseBattleSkill(string csv)
		{
			foreach (CsvRow row in ReadRows(csv))
			{
				string skillKey = row.Get("SkillKey");
				string classKey = row.Get("ClassKey");
				if (string.IsNullOrWhiteSpace(skillKey) || string.IsNullOrWhiteSpace(classKey))
					continue;

				BattleSkillDefinition skill = new BattleSkillDefinition(
					skillKey,
					classKey,
					_classKeyToPawnClass.TryGetValue(classKey, out PawnClass pawnClass) ? pawnClass : PawnClass.None,
					row.Get("SkillCategory"),
					row.GetInt("ActionSlot"),
					row.GetInt("ApCost"),
					row.GetInt("RangeMin"),
					row.GetInt("RangeMax"),
					row.Get("TargetType"),
					row.Get("TargetShape"),
					row.Get("RequiredOverlayType"),
					row.Get("EffectGroupKey"));

				_skillsByKey[skill.SkillKey] = skill;
				_skillsByClassSlot[BuildClassSlotKey(skill.ClassKey, skill.ActionSlot)] = skill;
			}
		}

		void ParseBattleSkillEffect(string csv)
		{
			foreach (CsvRow row in ReadRows(csv))
			{
				string effectGroupKey = row.Get("EffectGroupKey");
				string effectInstanceKey = row.Get("EffectInstanceKey");
				if (string.IsNullOrWhiteSpace(effectGroupKey) || string.IsNullOrWhiteSpace(effectInstanceKey))
					continue;

				BattleSkillEffectDefinition effect = new BattleSkillEffectDefinition(
					effectGroupKey,
					effectInstanceKey,
					row.GetInt("EffectOrder"),
					row.Get("EffectKey"),
					row.Get("Trigger"),
					row.Get("EffectTarget"),
					row.Get("TargetSkillKey"),
					row.Get("ExclusiveGroup"),
					row.GetInt("ExclusivePriority"),
					row.GetBool("StopOnMatch"));

				if (_effectsByGroup.TryGetValue(effectGroupKey, out List<BattleSkillEffectDefinition> effects) == false)
				{
					effects = new List<BattleSkillEffectDefinition>();
					_effectsByGroup[effectGroupKey] = effects;
				}

				effects.Add(effect);
			}
		}

		void ParseBattleSkillEffectParam(string csv)
		{
			foreach (CsvRow row in ReadRows(csv))
			{
				string effectGroupKey = row.Get("EffectGroupKey");
				string effectInstanceKey = row.Get("EffectInstanceKey");
				string paramKey = row.Get("ParamKey");
				if (string.IsNullOrWhiteSpace(effectGroupKey)
					|| string.IsNullOrWhiteSpace(effectInstanceKey)
					|| string.IsNullOrWhiteSpace(paramKey))
				{
					continue;
				}

				string key = BuildEffectInstanceKey(effectGroupKey, effectInstanceKey);
				if (_effectParamsByInstance.TryGetValue(key, out Dictionary<string, string> parameters) == false)
				{
					parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					_effectParamsByInstance[key] = parameters;
				}

				parameters[paramKey] = row.Get("ParamValue");
			}
		}

		void ParseBattleSkillView(string csv)
		{
			foreach (CsvRow row in ReadRows(csv))
			{
				string skillKey = row.Get("SkillKey");
				if (string.IsNullOrWhiteSpace(skillKey))
					continue;

				_skillViewsBySkillKey[skillKey] = new BattleSkillViewDefinition(
					skillKey,
					row.Get("AnimTrigger"),
					row.Get("VfxKey"),
					row.Get("SfxKey"),
					row.Get("IconKey"),
					row.Get("ProjectileKey"));
			}
		}

		void ParseBattleZoc(string csv)
		{
			foreach (CsvRow row in ReadRows(csv))
			{
				string classKey = row.Get("ClassKey");
				if (string.IsNullOrWhiteSpace(classKey)
					|| _classKeyToPawnClass.TryGetValue(classKey, out PawnClass pawnClass) == false)
				{
					continue;
				}

				_zocProfilesByPawnClass[pawnClass] = new BattleZocDefinition(
					classKey,
					pawnClass,
					row.GetBool("Enabled"),
					row.GetInt("Range"),
					row.GetInt("FrontArcWidth"),
					row.GetInt("ReactionLimitPerTurn"),
					row.GetInt("ReactionSkillSlot"),
					row.Get("Triggers"));
			}
		}

		void ParseDisplayText(string csv)
		{
			foreach (CsvRow row in ReadRows(csv))
			{
				string ownerType = row.Get("OwnerType");
				string ownerKey = row.Get("OwnerKey");
				string textType = row.Get("TextType");
				if (string.IsNullOrWhiteSpace(ownerType)
					|| string.IsNullOrWhiteSpace(ownerKey)
					|| string.IsNullOrWhiteSpace(textType))
				{
					continue;
				}

				string key = BuildDisplayTextKey(ownerType, ownerKey);
				if (_displayTexts.TryGetValue(key, out BattleDisplayTextSet textSet) == false)
				{
					textSet = new BattleDisplayTextSet(ownerType, ownerKey);
					_displayTexts[key] = textSet;
				}

				textSet.Set(textType, row.Get("ko_KR"), row.Get("en_US"), row.Get("Note"));
			}
		}

		void ParseEnumDef(string csv)
		{
			foreach (CsvRow row in ReadRows(csv))
			{
				string enumName = row.Get("EnumName");
				string enumValue = row.Get("EnumValue");
				if (string.IsNullOrWhiteSpace(enumName) || string.IsNullOrWhiteSpace(enumValue))
					continue;

				if (_enumDefinitions.TryGetValue(enumName, out List<BattleEnumDefinition> definitions) == false)
				{
					definitions = new List<BattleEnumDefinition>();
					_enumDefinitions[enumName] = definitions;
				}

				definitions.Add(new BattleEnumDefinition(enumName, enumValue, row.Get("Description")));
			}
		}

		static async Task<string> LoadCsvTextAsync(TableAsset table)
		{
			AsyncOperationHandle<TextAsset> handle = Addressables.LoadAssetAsync<TextAsset>(table.Address);
			await handle.Task;

			if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
			{
				string text = handle.Result.text;
				Addressables.Release(handle);
				return text;
			}

			if (handle.IsValid())
				Addressables.Release(handle);

#if UNITY_EDITOR
			TextAsset editorAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(table.EditorPath);
			if (editorAsset != null)
				return editorAsset.text;
#endif
			Debug.LogWarning($"Failed to load GameData table from Addressables. address={table.Address}");
			return string.Empty;
		}

		static IEnumerable<CsvRow> ReadRows(string csv)
		{
			List<List<string>> lines = ParseCsv(csv);
			if (lines.Count == 0)
				yield break;

			List<string> headers = lines[0];
			if (headers.Count > 0)
				headers[0] = StripBom(headers[0]);

			for (int i = 1; i < lines.Count; i++)
			{
				List<string> values = lines[i];
				if (IsBlankRow(values) || IsTypeRow(values))
					continue;

				yield return new CsvRow(headers, values);
			}
		}

		static List<List<string>> ParseCsv(string text)
		{
			List<List<string>> rows = new List<List<string>>();
			List<string> row = new List<string>();
			StringBuilder cell = new StringBuilder();
			bool inQuotes = false;

			for (int i = 0; i < text.Length; i++)
			{
				char ch = text[i];
				if (inQuotes)
				{
					if (ch == '"')
					{
						if (i + 1 < text.Length && text[i + 1] == '"')
						{
							cell.Append('"');
							i++;
						}
						else
						{
							inQuotes = false;
						}
					}
					else
					{
						cell.Append(ch);
					}

					continue;
				}

				if (ch == '"')
				{
					inQuotes = true;
				}
				else if (ch == ',')
				{
					row.Add(cell.ToString());
					cell.Length = 0;
				}
				else if (ch == '\r')
				{
					if (i + 1 < text.Length && text[i + 1] == '\n')
						i++;

					row.Add(cell.ToString());
					cell.Length = 0;
					rows.Add(row);
					row = new List<string>();
				}
				else if (ch == '\n')
				{
					row.Add(cell.ToString());
					cell.Length = 0;
					rows.Add(row);
					row = new List<string>();
				}
				else
				{
					cell.Append(ch);
				}
			}

			if (cell.Length > 0 || row.Count > 0)
			{
				row.Add(cell.ToString());
				rows.Add(row);
			}

			return rows;
		}

		static bool IsBlankRow(List<string> values)
		{
			for (int i = 0; i < values.Count; i++)
			{
				if (string.IsNullOrWhiteSpace(values[i]) == false)
					return false;
			}

			return true;
		}

		static bool IsTypeRow(List<string> values)
		{
			if (values.Count == 0)
				return false;

			for (int i = 0; i < values.Count; i++)
			{
				string value = values[i].Trim();
				if (value != "string"
					&& value != "enum"
					&& value != "int"
					&& value != "bool"
					&& value != "float")
				{
					return false;
				}
			}

			return true;
		}

		static string StripBom(string value)
		{
			if (string.IsNullOrEmpty(value) == false && value[0] == '\ufeff')
				return value.Substring(1);

			return value;
		}

		static bool TryParsePawnClass(string value, out PawnClass pawnClass)
		{
			pawnClass = PawnClass.None;
			string normalizedValue = NormalizeEnumToken(value)
				.Replace("PAWNCLASS", string.Empty, StringComparison.OrdinalIgnoreCase);

			foreach (PawnClass candidate in Enum.GetValues(typeof(PawnClass)))
			{
				if (NormalizeEnumToken(candidate.ToString()) == normalizedValue)
				{
					pawnClass = candidate;
					return true;
				}
			}

			return false;
		}

		static BattlePawnRole ParseRole(string value)
		{
			string normalizedValue = NormalizeEnumToken(value)
				.Replace("BATTLEPAWNROLE", string.Empty, StringComparison.OrdinalIgnoreCase);

			foreach (BattlePawnRole candidate in Enum.GetValues(typeof(BattlePawnRole)))
			{
				if (NormalizeEnumToken(candidate.ToString()) == normalizedValue)
					return candidate;
			}

			return BattlePawnRole.None;
		}

		static string NormalizeEnumToken(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return string.Empty;

			StringBuilder builder = new StringBuilder(value.Length);
			for (int i = 0; i < value.Length; i++)
			{
				char ch = value[i];
				if (char.IsLetterOrDigit(ch))
					builder.Append(char.ToUpperInvariant(ch));
			}

			return builder.ToString();
		}

		static string BuildClassSlotKey(string classKey, int actionSlot)
		{
			return $"{classKey}:{actionSlot.ToString(CultureInfo.InvariantCulture)}";
		}

		static string BuildEffectInstanceKey(string effectGroupKey, string effectInstanceKey)
		{
			return $"{effectGroupKey}:{effectInstanceKey}";
		}

		static string BuildDisplayTextKey(string ownerType, string ownerKey)
		{
			return $"{ownerType}:{ownerKey}";
		}

		readonly struct TableAsset
		{
			public readonly string Address;
			public readonly string EditorPath;

			public TableAsset(string address, string editorPath)
			{
				Address = address;
				EditorPath = editorPath;
			}
		}

		readonly struct CsvRow
		{
			readonly List<string> _headers;
			readonly List<string> _values;

			public CsvRow(List<string> headers, List<string> values)
			{
				_headers = headers;
				_values = values;
			}

			public string Get(string columnName)
			{
				for (int i = 0; i < _headers.Count; i++)
				{
					if (string.Equals(_headers[i], columnName, StringComparison.OrdinalIgnoreCase))
						return i < _values.Count ? _values[i].Trim() : string.Empty;
				}

				return string.Empty;
			}

			public int GetInt(string columnName)
			{
				return int.TryParse(Get(columnName), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
					? value
					: 0;
			}

			public bool GetBool(string columnName)
			{
				string value = Get(columnName);
				return string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase)
					|| string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
					|| value == "1";
			}
		}

		sealed class EmptyStringDictionary : Dictionary<string, string>
		{
			public static readonly EmptyStringDictionary Instance = new EmptyStringDictionary();

			EmptyStringDictionary()
				: base(StringComparer.OrdinalIgnoreCase)
			{
			}
		}
	}

	public sealed class BattlePawnTemplateDefinition
	{
		public readonly string ClassKey;
		public readonly PawnClass PawnClass;
		public readonly BattlePawnRole Role;
		public readonly int BaseStr;
		public readonly int BaseCon;
		public readonly int BaseDex;
		public readonly int BaseSpell;
		public readonly int BaseDefense;
		public readonly int BaseFocus;
		public readonly int BaseWill;

		public BattlePawnTemplateDefinition(
			string classKey,
			PawnClass pawnClass,
			BattlePawnRole role,
			int baseStr,
			int baseCon,
			int baseDex,
			int baseSpell,
			int baseDefense,
			int baseFocus,
			int baseWill)
		{
			ClassKey = classKey;
			PawnClass = pawnClass;
			Role = role;
			BaseStr = baseStr;
			BaseCon = baseCon;
			BaseDex = baseDex;
			BaseSpell = baseSpell;
			BaseDefense = baseDefense;
			BaseFocus = baseFocus;
			BaseWill = baseWill;
		}
	}

	public sealed class BattleZocDefinition
	{
		public readonly string ClassKey;
		public readonly PawnClass PawnClass;
		public readonly bool Enabled;
		public readonly int Range;
		public readonly int FrontArcWidth;
		public readonly int ReactionLimitPerTurn;
		public readonly int ReactionSkillSlot;
		public readonly string Triggers;

		public BattleZocDefinition(
			string classKey,
			PawnClass pawnClass,
			bool enabled,
			int range,
			int frontArcWidth,
			int reactionLimitPerTurn,
			int reactionSkillSlot,
			string triggers)
		{
			ClassKey = classKey;
			PawnClass = pawnClass;
			Enabled = enabled;
			Range = range;
			FrontArcWidth = frontArcWidth;
			ReactionLimitPerTurn = reactionLimitPerTurn;
			ReactionSkillSlot = reactionSkillSlot;
			Triggers = triggers ?? string.Empty;
		}

		// Current server data expresses an opportunity attack as ENEMY_MOVE_IN_ZONE:
		// an enemy moves while occupying this pawn's ZoC. LEAVE_ZONE remains accepted
		// for older data sets which used that terminology.
		public bool TriggersOnEnemyMove => Triggers.IndexOf("ENEMY_MOVE_IN_ZONE", StringComparison.OrdinalIgnoreCase) >= 0
			|| Triggers.IndexOf("LEAVE_ZONE", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	public sealed class BattleSkillDefinition
	{
		public readonly string SkillKey;
		public readonly string ClassKey;
		public readonly PawnClass PawnClass;
		public readonly string SkillCategory;
		public readonly int ActionSlot;
		public readonly int ApCost;
		public readonly int RangeMin;
		public readonly int RangeMax;
		public readonly string TargetType;
		public readonly string TargetShape;
		public readonly string RequiredOverlayType;
		public readonly string EffectGroupKey;

		public BattleSkillDefinition(
			string skillKey,
			string classKey,
			PawnClass pawnClass,
			string skillCategory,
			int actionSlot,
			int apCost,
			int rangeMin,
			int rangeMax,
			string targetType,
			string targetShape,
			string requiredOverlayType,
			string effectGroupKey)
		{
			SkillKey = skillKey;
			ClassKey = classKey;
			PawnClass = pawnClass;
			SkillCategory = skillCategory;
			ActionSlot = actionSlot;
			ApCost = apCost;
			RangeMin = rangeMin;
			RangeMax = rangeMax;
			TargetType = targetType;
			TargetShape = targetShape;
			RequiredOverlayType = requiredOverlayType;
			EffectGroupKey = effectGroupKey;
		}
	}

	public sealed class BattleSkillEffectDefinition
	{
		public readonly string EffectGroupKey;
		public readonly string EffectInstanceKey;
		public readonly int EffectOrder;
		public readonly string EffectKey;
		public readonly string Trigger;
		public readonly string EffectTarget;
		public readonly string TargetSkillKey;
		public readonly string ExclusiveGroup;
		public readonly int ExclusivePriority;
		public readonly bool StopOnMatch;

		public BattleSkillEffectDefinition(
			string effectGroupKey,
			string effectInstanceKey,
			int effectOrder,
			string effectKey,
			string trigger,
			string effectTarget,
			string targetSkillKey,
			string exclusiveGroup,
			int exclusivePriority,
			bool stopOnMatch)
		{
			EffectGroupKey = effectGroupKey;
			EffectInstanceKey = effectInstanceKey;
			EffectOrder = effectOrder;
			EffectKey = effectKey;
			Trigger = trigger;
			EffectTarget = effectTarget;
			TargetSkillKey = targetSkillKey;
			ExclusiveGroup = exclusiveGroup;
			ExclusivePriority = exclusivePriority;
			StopOnMatch = stopOnMatch;
		}
	}

	public sealed class BattleSkillViewDefinition
	{
		public readonly string SkillKey;
		public readonly string AnimTrigger;
		public readonly string VfxKey;
		public readonly string SfxKey;
		public readonly string IconKey;
		public readonly string ProjectileKey;

		public BattleSkillViewDefinition(string skillKey, string animTrigger, string vfxKey, string sfxKey, string iconKey, string projectileKey)
		{
			SkillKey = skillKey;
			AnimTrigger = animTrigger;
			VfxKey = vfxKey;
			SfxKey = sfxKey;
			IconKey = iconKey;
			ProjectileKey = projectileKey;
		}
	}

	public sealed class BattleDisplayTextSet
	{
		readonly Dictionary<string, BattleLocalizedText> _texts = new Dictionary<string, BattleLocalizedText>(StringComparer.OrdinalIgnoreCase);

		public readonly string OwnerType;
		public readonly string OwnerKey;

		public BattleDisplayTextSet(string ownerType, string ownerKey)
		{
			OwnerType = ownerType;
			OwnerKey = ownerKey;
		}

		public void Set(string textType, string koKr, string enUs, string note)
		{
			_texts[textType] = new BattleLocalizedText(koKr, enUs, note);
		}

		public bool TryGet(string textType, out BattleLocalizedText text)
		{
			return _texts.TryGetValue(textType, out text);
		}
	}

	public readonly struct BattleLocalizedText
	{
		public readonly string KoKr;
		public readonly string EnUs;
		public readonly string Note;

		public BattleLocalizedText(string koKr, string enUs, string note)
		{
			KoKr = koKr;
			EnUs = enUs;
			Note = note;
		}
	}

	public readonly struct BattleEnumDefinition
	{
		public readonly string EnumName;
		public readonly string EnumValue;
		public readonly string Description;

		public BattleEnumDefinition(string enumName, string enumValue, string description)
		{
			EnumName = enumName;
			EnumValue = enumValue;
			Description = description;
		}
	}
}
