using UnityEngine;
using UnityEngine.UI;

namespace Field
{
	/// <summary>Serialized references authored on the FieldBattleClassSelectionUI prefab.</summary>
	[DisallowMultipleComponent]
	public sealed class FieldBattleClassSelectionView : MonoBehaviour
	{
		[SerializeField] Canvas _canvas;
		[SerializeField] GameObject _panel;
		[SerializeField] Text _status;
		[SerializeField] Transform _options;
		[SerializeField] Button _submit;
		[SerializeField] Text _submitText;

		public Canvas Canvas => _canvas;
		public GameObject Panel => _panel;
		public Text Status => _status;
		public Transform Options => _options;
		public Button Submit => _submit;
		public Text SubmitText => _submitText;

		// Used only by the editor setup command while authoring the prefab.
		public void Configure(Canvas canvas, GameObject panel, Text status, Transform options, Button submit, Text submitText)
		{
			_canvas = canvas;
			_panel = panel;
			_status = status;
			_options = options;
			_submit = submit;
			_submitText = submitText;
		}
	}
}
