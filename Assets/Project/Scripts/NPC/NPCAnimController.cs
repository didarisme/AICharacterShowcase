using UnityEngine;
using DynamicNpcs;
using System.Collections;

public class NPCAnimController : MonoBehaviour
{
    public enum Mode { Idle, Thinking, Talking }

    [SerializeField] private NPCDialogueAgent agent;
    [SerializeField] private Animator animator;
    [SerializeField] private GameObject thinkingFx;
    [SerializeField] private int idleVariants = 2;
    [SerializeField] private int talkVariants = 2;

    static readonly int IsTalkingHash = Animator.StringToHash("IsTalking");
    static readonly int VariantHash = Animator.StringToHash("AnimVariant");

    private Mode mode = Mode.Idle;
    private Coroutine variantRoutine;

    private void Reset()
    {
        agent = GetComponent<NPCDialogueAgent>();
        animator = GetComponentInChildren<Animator>();
    }

    private void Awake()
    {
        if (agent == null) agent = GetComponent<NPCDialogueAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    private void OnEnable()
    {
        if (agent == null) return;
        agent.onRequestStarted.AddListener(OnRequestStarted);
        agent.onSentenceStarted.AddListener(OnSentenceStarted);
        agent.onSpeechFinished.AddListener(OnSpeechFinished);
        agent.onError.AddListener(OnError);
    }

    private void OnDisable()
    {
        if (agent == null) return;
        agent.onRequestStarted.RemoveListener(OnRequestStarted);
        agent.onSentenceStarted.RemoveListener(OnSentenceStarted);
        agent.onSpeechFinished.RemoveListener(OnSpeechFinished);
        agent.onError.RemoveListener(OnError);
    }

    private void Start()
    {
        if (thinkingFx != null)
            thinkingFx.SetActive(false);

        StartVariantRoutine();
    }

    // Dialogue events
    private void OnRequestStarted() => SetMode(Mode.Thinking);
    private void OnSentenceStarted(string _) => SetMode(Mode.Talking);
    private void OnSpeechFinished() => SetMode(Mode.Idle);
    private void OnError(string _) => SetMode(Mode.Idle);

    // Mode
    public void SetMode(Mode newMode)
    {
        if (newMode == mode) return;

        bool wasTalking = mode == Mode.Talking;
        bool isTalking = newMode == Mode.Talking;
        mode = newMode;

        if (wasTalking != isTalking)
        {
            animator.SetBool(IsTalkingHash, isTalking);
            animator.SetInteger(VariantHash, RandomVariant());
        }

        if (thinkingFx != null)
            thinkingFx.SetActive(newMode == Mode.Thinking);

        StartVariantRoutine();
    }

    // Animation variants
    private void StartVariantRoutine()
    {
        StopVariantRoutine();

        variantRoutine = StartCoroutine(VariantRoutine());
    }

    private void StopVariantRoutine()
    {
        if (variantRoutine != null)
        {
            StopCoroutine(variantRoutine);
            variantRoutine = null;
        }
    }

    private IEnumerator VariantRoutine()
    {
        while (true)
        {
            yield return null;

            while (animator.IsInTransition(0))
                yield return null;

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            float duration = state.length;

            yield return new WaitForSeconds(duration);

            animator.SetInteger(VariantHash, RandomVariant());
        }
    }

    private int RandomVariant() => Random.Range(0, mode == Mode.Talking ? talkVariants : idleVariants);
}
