using TMPro;
using UnityEngine;

public class Script_Clock : MonoBehaviour
{
    [SerializeField] private int endHour = 6;
    private bool finished;

    private float gameMinutes;

    [Header("Clock Hands")]
    [SerializeField] private Transform hourHand;
    private Vector3 hourHandStartEuler;
    private int lastDisplayedMinute = -1;
    [SerializeField] private Transform minuteHand;
    private Vector3 minuteHandStartEuler;
    [Header("Time Settings")]
    [SerializeField] private float realMinutesPerGameHour = 25f;
    [SerializeField] private int startHour = 0;

    private TMP_Text timeText;

    [Header("Text Display")]
    [SerializeField] private GameObject timeTextObject;

    public float CurrentGameMinutes => gameMinutes;
    public bool IsFinished => finished;

    private void UpdateHands()
    {
        float minuteAngle = (gameMinutes % 60f) * 6f;
        float hourAngle = ((gameMinutes / 60f) % 12f) * 30f;

        if (minuteHand != null)
            minuteHand.localEulerAngles = new Vector3(minuteHandStartEuler.x + minuteAngle, minuteHandStartEuler.y, minuteHandStartEuler.z);

        if (hourHand != null)
            hourHand.localEulerAngles = new Vector3(hourHandStartEuler.x + hourAngle, hourHandStartEuler.y, hourHandStartEuler.z);
    }

    private void UpdateText()
    {
        if (timeText == null) return;

        int totalMinutes = Mathf.FloorToInt(gameMinutes);
        if (totalMinutes == lastDisplayedMinute) return;
        lastDisplayedMinute = totalMinutes;

        int hours = (totalMinutes / 60) % 24;
        int minutes = totalMinutes % 60;
        string suffix = hours < 12 ? "am" : "pm";

        timeText.text = $"Time: {hours:00}:{minutes:00} {suffix}";
    }

    void Start()
    {
        if (timeTextObject != null)
            timeText = timeTextObject.GetComponent<TMP_Text>();

        if (hourHand != null) hourHandStartEuler = hourHand.localEulerAngles;
        if (minuteHand != null) minuteHandStartEuler = minuteHand.localEulerAngles;

        gameMinutes = startHour * 60f;
        UpdateText();
        UpdateHands();
    }

    void Update()
    {
        if (finished) return;

        float gameMinutesPerRealSecond = 60f / (realMinutesPerGameHour * 60f);
        gameMinutes += Time.deltaTime * gameMinutesPerRealSecond;

        float endMinutes = endHour * 60f;
        if (gameMinutes >= endMinutes)
        {
            gameMinutes = endMinutes;
            finished = true;
        }

        UpdateText();
        UpdateHands();
    }
}
