using Godot;

/// <summary>Scene-local interaction contract for objects that can be activated by a 3D interactor.</summary>
public interface IInteractable
{
    /// <summary>Performs the local scene interaction initiated by the specified node.</summary>
    void Interact(Node3D interactor);
}
