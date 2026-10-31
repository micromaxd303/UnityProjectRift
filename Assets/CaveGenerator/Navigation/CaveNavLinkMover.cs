using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Прыжок наземного врага по ссылке NavMesh: дуга вместо скольжения по прямой.
/// Вниз — короткий подскок и падение, вверх — прыжок с запасом над краем.
/// Вешается рядом с NavMeshAgent. Анимацию можно повесить на JumpStarted / JumpFinished.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class CaveNavLinkMover : MonoBehaviour
{
    [Tooltip("Ускорение падения, м/с².")]
    [Min(1f)] [SerializeField] private float gravity = 20f;

    [Tooltip("Подскок над верхней точкой дуги, м.")]
    [Min(0f)] [SerializeField] private float arcHeight = 0.5f;

    [Tooltip("Повернуться лицом к точке приземления перед прыжком.")]
    [SerializeField] private bool faceLanding = true;

    private NavMeshAgent _agent;

    public bool IsJumping { get; private set; }
    public event System.Action<Vector3, Vector3> JumpStarted;
    public event System.Action JumpFinished;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.autoTraverseOffMeshLink = false;
    }

    private void Update()
    {
        if (!IsJumping && _agent.isOnNavMesh && _agent.isOnOffMeshLink)
            StartCoroutine(Jump(_agent.currentOffMeshLinkData));
    }

    private IEnumerator Jump(OffMeshLinkData link)
    {
        IsJumping = true;
        Vector3 offset = Vector3.up * _agent.baseOffset;
        Vector3 start = transform.position;
        Vector3 end = link.endPos + offset;

        // Вершина дуги: над более высокой точкой на arcHeight
        float apex = Mathf.Max(start.y, end.y) + arcHeight;
        float up = Mathf.Sqrt(2f * gravity * Mathf.Max(0.01f, apex - start.y));
        float down = Mathf.Sqrt(2f * gravity * Mathf.Max(0.01f, apex - end.y));
        float duration = (up + down) / gravity;

        if (faceLanding)
        {
            var flat = end - start;
            flat.y = 0f;
            if (flat.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(flat);
        }

        JumpStarted?.Invoke(start, end);

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            var p = Vector3.Lerp(start, end, k);
            // Вертикаль по баллистике: y(t) = y0 + v·t − g·t²/2
            p.y = start.y + up * t - 0.5f * gravity * t * t;
            transform.position = p;
            yield return null;
        }

        transform.position = end;
        _agent.CompleteOffMeshLink();
        IsJumping = false;
        JumpFinished?.Invoke();
    }

    private void OnDisable() => IsJumping = false;
}
