using UnityEngine;

// Sits on every child square of a BlockPiece.
// Forwards Unity's mouse messages up to the parent BlockPiece.
// Requires: BoxCollider2D on the same GameObject (added by BlockPiece).
[RequireComponent(typeof(BoxCollider2D))]
public class SquareDragProxy : MonoBehaviour
{
    BlockPiece _piece;

    void Awake()
    {
        // BlockPiece lives on the parent GameObject
        _piece = GetComponentInParent<BlockPiece>();
    }

    void OnMouseDown() { if (_piece != null) _piece.BeginDrag(); }
    void OnMouseDrag() { if (_piece != null) _piece.DuringDrag(); }
    void OnMouseUp()   { if (_piece != null) _piece.EndDrag();   }
}