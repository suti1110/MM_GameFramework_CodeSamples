using DG.Tweening;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

public static class PlayerAnimationHash
{
    public static readonly int DirX = Animator.StringToHash("DirX");
    public static readonly int DirY = Animator.StringToHash("DirY");
    public static readonly int IsGrounded = Animator.StringToHash("IsGround");
    public static readonly int IsJumping = Animator.StringToHash("IsJumping");
    public static readonly int IsAttacking = Animator.StringToHash("IsAttacking");
    public static readonly int IsTakingDamage = Animator.StringToHash("IsTakingDamage");
}

public static class AnimationLayerManager
{
    // 부드럽게 변경
    public static void SetLayerWeight(this Animator anim, int layer, float weight, float duration)
    {
        string tweenId = anim.GetInstanceID() + "_" + layer;

        DOTween.Kill(tweenId);

        DOTween.To(
            () => anim.GetLayerWeight(layer),
            x => anim.SetLayerWeight(layer, x),
            weight,
            duration
        );
    }
}

[RequireComponent(typeof(Player))]
public class PlayerAnimation : MonoBehaviour
{
    private readonly PlayerStateManager _state = PlayerStateManager.Instance;

    private readonly PlayerJumpingStateManager _jumpingState = PlayerJumpingStateManager.Instance;

    [SerializeField]
    private Animator _anim;
    private IPlayerMovingInput _playerMovingInput;
    private ISkillAnimation _skillAnimation;

    // --- Playables API 변수 ---
    private PlayableGraph _graph; // 전체 애니메이션 Playable 노드들을 관리하는 그래프
    private AnimationLayerMixerPlayable _mixer; // 0번엔 기존 컨트롤러, 1번엔 스킬 클립을 넣어 섞어주는 믹서
    private AnimationClipPlayable _currentSkillPlayable; // 현재 동적으로 주입된 스킬 애니메이션 클립 노드

    private InputAction _horizontal;
    private InputAction _vertical;

    [Header("InputAnimation")]
    [SerializeField]
    private float _inputSensitivity = 3f;

    private float _currentDirX;
    private float _currentDirY;

    private void Awake()
    {
        _state[gameObject].OnStateChanged += OnStateChanged;
        _jumpingState[gameObject].OnJumpingStateChanged += OnJumpingStateChanged;
    }

    private void Start()
    {
        _playerMovingInput = GetComponent<IPlayerMovingInput>();
        _skillAnimation = GetComponent<ISkillAnimation>();
        _horizontal = _playerMovingInput.Moving.Move.Horizontal;
        _vertical = _playerMovingInput.Moving.Move.Vertical;

        // --- Playables API 초기화 세팅 ---
        // 기존 Animator Controller의 트랜지션(거미줄) 한계를 피하기 위해
        // 런타임에 PlayableGraph를 직접 생성하여 스킬 애니메이션을 오버라이드할 준비를 합니다.
        _graph = PlayableGraph.Create("PlayerAnimationGraph");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(_graph, "Animation", _anim);
        _mixer = AnimationLayerMixerPlayable.Create(_graph, 2);
        output.SetSourcePlayable(_mixer);

        // Input 0: 기존 이동/공격 로직이 담긴 원래의 AnimatorController를 그대로 연결
        if (_anim.runtimeAnimatorController != null)
        {
            var controllerPlayable = AnimatorControllerPlayable.Create(
                _graph,
                _anim.runtimeAnimatorController
            );
            _graph.Connect(controllerPlayable, 0, _mixer, 0);
            _mixer.SetInputWeight(0, 1f); // 평소에는 기존 컨트롤러 로직(Input 0)만 보이게 가중치 1 설정
        }
        _graph.Play();
    }

    // 연속적으로 바뀔 수 있는 요소들
    private void Update()
    {
        _currentDirX = Mathf.MoveTowards(
            _currentDirX,
            _horizontal.ReadValue<float>(),
            _inputSensitivity * Time.deltaTime
        );
        _currentDirY = Mathf.MoveTowards(
            _currentDirY,
            _vertical.ReadValue<float>(),
            _inputSensitivity * Time.deltaTime
        );

        _anim.SetFloat(PlayerAnimationHash.DirX, _currentDirX);
        _anim.SetFloat(PlayerAnimationHash.DirY, _currentDirY);
    }

    // 즉각적으로 바뀌어야 하는 요소들
    private void OnStateChanged(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.Idle:
                _anim.SetBool(PlayerAnimationHash.IsAttacking, false);
                _anim.SetBool(PlayerAnimationHash.IsTakingDamage, false);
                ResetSkillMixer();
                break;
            case PlayerState.Attacking:
                _anim.SetBool(PlayerAnimationHash.IsAttacking, true);
                _anim.SetBool(PlayerAnimationHash.IsTakingDamage, false);
                ResetSkillMixer();
                break;
            case PlayerState.TakingDamage:
                _anim.SetBool(PlayerAnimationHash.IsAttacking, false);
                _anim.SetBool(PlayerAnimationHash.IsTakingDamage, true);
                ResetSkillMixer();
                break;
            case PlayerState.UsingSkill:
                // 스킬 발동 시: 해당 스킬의 애니메이션 클립을 가져와 임시 Playable 노드로 만든 뒤,
                // 기존 컨트롤러(Input 0)를 숨기고 스킬 애니메이션(Input 1)을 100% 가중치로 재생합니다.
                AnimationClip clip = _skillAnimation.GetSkillClip();
                if (clip != null)
                {
                    if (_currentSkillPlayable.IsValid())
                        _currentSkillPlayable.Destroy(); // 이전 스킬 클립 찌꺼기 제거

                    _currentSkillPlayable = AnimationClipPlayable.Create(_graph, clip);
                    _graph.Connect(_currentSkillPlayable, 0, _mixer, 1);
                    _mixer.SetInputWeight(0, 0f); // 기본 걷기/점프 숨김
                    _mixer.SetInputWeight(1, 1f); // 스킬 애니메이션 강제 덮어쓰기
                }
                break;
            case PlayerState.Dead:
                _anim.SetBool(PlayerAnimationHash.IsAttacking, false);
                _anim.SetBool(PlayerAnimationHash.IsTakingDamage, false);
                ResetSkillMixer();
                break;
        }
    }

    private void ResetSkillMixer()
    {
        // 스킬이 아닌 다른 상태로 돌아갈 때, 다시 Input 0(기존 컨트롤러) 가중치를 1로 돌려 부드럽게 복구시킵니다.
        if (_mixer.IsValid())
        {
            _mixer.SetInputWeight(0, 1f);
            _mixer.SetInputWeight(1, 0f);
        }
    }

    private void OnJumpingStateChanged(PlayerJumpingState jumpingState)
    {
        switch (jumpingState)
        {
            case PlayerJumpingState.Idle:
                _anim.SetBool(PlayerAnimationHash.IsGrounded, true);
                _anim.SetBool(PlayerAnimationHash.IsJumping, false);
                break;
            case PlayerJumpingState.Falling:
                _anim.SetBool(PlayerAnimationHash.IsGrounded, false);
                _anim.SetBool(PlayerAnimationHash.IsJumping, false);
                break;
            case PlayerJumpingState.Jumping:
                _anim.SetBool(PlayerAnimationHash.IsGrounded, false);
                _anim.SetBool(PlayerAnimationHash.IsJumping, true);
                break;
            case PlayerJumpingState.UsingSkill:
                _anim.SetBool(PlayerAnimationHash.IsGrounded, true);
                _anim.SetBool(PlayerAnimationHash.IsJumping, false);
                break;
            case PlayerJumpingState.Dead:
                _anim.SetBool(PlayerAnimationHash.IsGrounded, true);
                _anim.SetBool(PlayerAnimationHash.IsJumping, false);
                break;
        }
    }

    private void OnValidate()
    {
        if (!_anim)
        {
            EditorLog.LogError("Animator가 할당되지 않았습니다!", this);
        }
    }

    private void OnDestroy()
    {
        if (_graph.IsValid())
        {
            _graph.Destroy();
        }

        _state[gameObject].OnStateChanged -= OnStateChanged;
        _jumpingState[gameObject].OnJumpingStateChanged -= OnJumpingStateChanged;
    }
}
