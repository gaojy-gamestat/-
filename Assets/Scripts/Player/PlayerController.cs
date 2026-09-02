using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 最小可用的 Host-authoritative 玩家控制器：
/// - Owner 只负责采集输入，通过 ServerRpc 提交给 Host；
/// - 只有 Host（服务器）执行 CharacterController.Move 和旋转；
/// - 位置/旋转通过 NetworkTransform（Server Authority）同步给对方。
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(NetworkObject))]
public class PlayerController : NetworkBehaviour
{
    [Header("移动参数")]
    [SerializeField]
    private float moveSpeed = 3.5f;

    [SerializeField]
    private float rotateSpeed = 120f;

    [SerializeField]
    private float gravity = -9.81f;

    private CharacterController m_Controller;
    private float m_VerticalVelocity;

    private void Awake()
    {
        m_Controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        // 输入采集（Owner 本地）
        float moveX = 0f;
        float moveZ = 0f;
        float rotY = 0f;

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) moveZ += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) moveZ -= 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) moveX -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) moveX += 1f;
        if (Input.GetKey(KeyCode.Q)) rotY -= 1f;
        if (Input.GetKey(KeyCode.E)) rotY += 1f;

        var input = new Vector2(moveX, moveZ);

        if (IsServer)
        {
            // Host 自己的玩家：直接在服务器上执行。
            ApplyMovement(input, rotY);
        }
        else
        {
            // Client：把输入提交给 Host，由 Host 权威执行。
            MoveServerRpc(input, rotY);
        }
    }

    [ServerRpc]
    private void MoveServerRpc(Vector2 input, float rotY)
    {
        ApplyMovement(input, rotY);
    }

    /// <summary>仅测试用：Host 侧直接驱动本机玩家（等价本地输入，仍走服务器权威路径）。</summary>
    public void TestMove(Vector2 input, float rotY)
    {
        if (IsServer)
        {
            ApplyMovement(input, rotY);
        }
    }

    /// <summary>重置垂直速度（场景切换传送玩家时调用，防止残留下坠速度隧穿地面）。</summary>
    public void ResetVerticalVelocity()
    {
        m_VerticalVelocity = 0f;
    }

    /// <summary>只在 Host 上执行。</summary>
    private void ApplyMovement(Vector2 input, float rotYInput)
    {
        if (m_Controller == null)
        {
            m_Controller = GetComponent<CharacterController>();
        }

        Vector3 move = (transform.right * input.x + transform.forward * input.y);
        if (move.sqrMagnitude > 1f)
        {
            move.Normalize();
        }

        if (m_Controller.isGrounded && m_VerticalVelocity < 0f)
        {
            m_VerticalVelocity = -2f;
        }

        m_VerticalVelocity += gravity * Time.deltaTime;
        // 限制终端速度，避免长时间下坠后一帧隧穿薄地面。
        m_VerticalVelocity = Mathf.Max(m_VerticalVelocity, -30f);
        move.y = m_VerticalVelocity;

        m_Controller.Move(move * (moveSpeed * Time.deltaTime));

        if (Mathf.Abs(rotYInput) > 0.01f)
        {
            transform.Rotate(0f, rotYInput * rotateSpeed * Time.deltaTime, 0f);
        }
    }
}
