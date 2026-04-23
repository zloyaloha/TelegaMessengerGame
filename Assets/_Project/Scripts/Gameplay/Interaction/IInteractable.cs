public interface IInteractable
{
    bool CanInteract(PlayerInteractor interactor);
    void Interact(PlayerInteractor interactor);
    string GetInteractionLabel(PlayerInteractor interactor);
}
