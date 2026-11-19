using System.Collections;
using TMPro;
using UnityEngine;

public class DialogueManager : MonoBehaviour
{

    [Header("FMOD Dialogue")]
    [Tooltip("GameObject with the FMOD Event Emitter (sequential multi instrument). " +
             "Emitter settings: Play Event = Object Enable, Stop Event = Object Disable. " +
             "Keep this object disabled in the scene.")]
    public GameObject FMOD_Event_Emitter_TV;

    [Header("VO Loop")]
    [Tooltip("This GameObject will be disabled when the dialogue starts.")]
    public GameObject VO_Loop;

    [Header("Typewriter")]
    [Tooltip("Time between each character appearing.")]
    public float characterDelay = 0.03f;

    [Header("Dialogue Canvas")]
    [Tooltip("Canvas containing the dialogue Panel.")]
    public Canvas dialogueCanvas;
    [Header("Dialogue Objects")]
    [Tooltip("GameObjects containing the TMP_Text components for each dialogue line.")]
    public GameObject[] dialogueObjects;

    [Tooltip("Panel inside the dialogue Canvas.")]
    public GameObject dialoguePanel;

    // True once every dialogue object has been traversed.
    private bool allDialogueTraversed = false;

    private int currentDialogueIndex = -1;

    private bool dialogueActive = false;
    private bool isTyping = false;

    private Coroutine typingCoroutine;

    public bool AllDialogueTraversed
    {
        get { return allDialogueTraversed; }
    }

    public bool IsDialogueActive
    {
        get { return dialogueActive; }
    }

    public void AdvanceDialogue()
    {
        if (!dialogueActive)
        {
            return;
        }

        // First click while the text is still typing:
        // finish the current line (the audio keeps playing).
        if (isTyping)
        {
            FinishCurrentLine();
            return;
        }

        // Text is already finished, so move to the next GameObject.
        // The current audio is cut and the next clip starts.
        AdvanceToNextDialogue();
    }

    // Called by CursorCameraManager when C_TV is clicked.
    public void HandleTVClick()
    {
        // The main dialogue has already been completed.
        // Don't let the TV restart it.
        if (allDialogueTraversed)
        {
            return;
        }

        // Dialogue hasn't started yet.
        if (!dialogueActive)
        {
            StartDialogue();
            return;
        }

        // Dialogue is already running.
        AdvanceDialogue();
    }

    public void StartDialogue()
    {
        if (dialogueObjects == null ||
            dialogueObjects.Length == 0)
        {
            Debug.LogWarning(
                "DialogueManager: No dialogue objects have been assigned."
            );

            return;
        }

        dialogueActive = true;

        allDialogueTraversed = false;

        currentDialogueIndex = 0;

        // Disable the VO loop.
        if (VO_Loop != null)
        {
            VO_Loop.SetActive(false);
        }

        // Enable the Canvas.
        if (dialogueCanvas != null)
        {
            dialogueCanvas.gameObject.SetActive(true);
        }

        // Enable the Panel.
        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(true);
        }

        HideAllDialogueObjects();

        // Enable and type the first dialogue.
        ShowDialogueObject(currentDialogueIndex);
    }

    private void AdvanceToNextDialogue()
    {
        // Disable the current dialogue object.
        if (currentDialogueIndex >= 0 &&
            currentDialogueIndex < dialogueObjects.Length)
        {
            if (dialogueObjects[currentDialogueIndex] != null)
            {
                dialogueObjects[currentDialogueIndex].SetActive(false);
            }
        }

        // Move to the next element in the array.
        currentDialogueIndex++;

        // End if there are no more lines.
        if (currentDialogueIndex >= dialogueObjects.Length)
        {
            EndDialogue();
            return;
        }

        // Enable and type the next line.
        ShowDialogueObject(currentDialogueIndex);
    }

    private void EndDialogue()
    {
        dialogueActive = false;
        isTyping = false;

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        // The player clicked past the last main line: cut its audio.
        StopDialogueAudio();

        // Hide all dialogue text objects.
        HideAllDialogueObjects();

        // Hide the dialogue panel.
        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(false);
        }

        // The entire dialogue array has now been traversed.
        allDialogueTraversed = true;

        Debug.Log("[Dialogue] Dialogue ended: all lines traversed. TV clicks no longer advance dialogue.");

        currentDialogueIndex = -1;
    }

    private void FinishCurrentLine()
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        if (currentDialogueIndex < 0 ||
            currentDialogueIndex >= dialogueObjects.Length)
        {
            return;
        }

        GameObject currentObject =
            dialogueObjects[currentDialogueIndex];

        if (currentObject != null)
        {
            TMP_Text dialogueText =
                currentObject.GetComponentInChildren<TMP_Text>(true);

            if (dialogueText != null)
            {
                dialogueText.ForceMeshUpdate();

                dialogueText.maxVisibleCharacters =
                    dialogueText.textInfo.characterCount;
            }
        }

        isTyping = false;

        // Skipping the typewriter does not affect the audio.
    }

    private void HideAllDialogueObjects()
    {
        if (dialogueObjects == null)
        {
            return;
        }

        foreach (GameObject dialogueObject in dialogueObjects)
        {
            if (dialogueObject != null)
            {
                dialogueObject.SetActive(false);
            }
        }
    }

    private void PlayDialogueAudio()
    {
        // Never let two dialogue clips overlap:
        // disabling the emitter object stops the clip that is still playing.
        StopDialogueAudio();

        if (FMOD_Event_Emitter_TV == null)
        {
            Debug.LogWarning(
                "DialogueManager: No FMOD Event Emitter TV object has been assigned."
            );

            return;
        }

        // Enabling the object makes the emitter start the event.
        // The multi instrument is sequential, so every start plays the next clip.
        FMOD_Event_Emitter_TV.SetActive(true);
    }

    private void ShowDialogueObject(int index)
    {
        if (index < 0 ||
            index >= dialogueObjects.Length)
        {
            EndDialogue();
            return;
        }

        GameObject dialogueObject = dialogueObjects[index];

        if (dialogueObject == null)
        {
            Debug.LogWarning(
                "DialogueManager: Dialogue object at index " +
                index +
                " is null."
            );

            AdvanceToNextDialogue();
            return;
        }

        dialogueObject.SetActive(true);

        TMP_Text dialogueText =
            dialogueObject.GetComponentInChildren<TMP_Text>(true);

        if (dialogueText == null)
        {
            Debug.LogWarning(
                "DialogueManager: No TMP_Text found inside " +
                dialogueObject.name
            );

            return;
        }

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
        }

        typingCoroutine = StartCoroutine(
            TypeText(dialogueText)
        );

        // Play the FMOD dialogue event.
        // This also cuts the previous line's audio, so clips never overlap.
        PlayDialogueAudio();
    }

    private void Start()
    {
        HideAllDialogueObjects();

        // The TV emitter object must start disabled.
        StopDialogueAudio();

        if (dialogueCanvas != null)
        {
            dialogueCanvas.gameObject.SetActive(true);
        }

        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(false);
        }
    }

    private void StopDialogueAudio()
    {
        // Disabling the object makes the emitter stop the event.
        if (FMOD_Event_Emitter_TV != null)
        {
            FMOD_Event_Emitter_TV.SetActive(false);
        }
    }

    private IEnumerator TypeText(TMP_Text dialogueText)
    {
        isTyping = true;

        dialogueText.ForceMeshUpdate();

        dialogueText.maxVisibleCharacters = 0;

        int characterCount =
            dialogueText.textInfo.characterCount;

        for (int i = 0; i <= characterCount; i++)
        {
            dialogueText.maxVisibleCharacters = i;

            yield return new WaitForSeconds(characterDelay);
        }

        dialogueText.maxVisibleCharacters = characterCount;

        isTyping = false;
        typingCoroutine = null;

        // The typewriter has finished, but the audio keeps playing.
        // It is only stopped when the player clicks to advance.
    }
}