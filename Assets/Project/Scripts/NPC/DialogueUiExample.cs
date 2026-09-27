using UnityEngine;
using TMPro;
using DynamicNpcs;

public class DialogueUiExample : MonoBehaviour
{
    [SerializeField] private NPCDialogueAgent npc;
    [SerializeField] private TMP_InputField playerInput;
    
    public void SendMessageToNpc()
    {
        if (npc.IsBusy) return;
        npc.Ask(playerInput.text);
        playerInput.text = "";
    }
}