using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Tilemaps;

public class TileAddressablesTest : MonoBehaviour
{
    [SerializeField] private Tilemap tilemap;

    private async void Start()
    {
        AsyncOperationHandle<TileBase> handle =
            Addressables.LoadAssetAsync<TileBase>("Tile/grass");

        await handle.Task;

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError("타일 로드 실패: Tile/grass");
            return;
        }

        TileBase grassTile = handle.Result;

        tilemap.SetTile(new Vector3Int(0, 0, 0), grassTile);
        tilemap.SetTile(new Vector3Int(1, 0, 0), grassTile);
        tilemap.SetTile(new Vector3Int(0, 1, 0), grassTile);
        tilemap.SetTile(new Vector3Int(-1, 0, 0), grassTile);
        tilemap.SetTile(new Vector3Int(0, -1, 0), grassTile);

        Debug.Log("Addressables 타일 로드 성공!");
    }
}