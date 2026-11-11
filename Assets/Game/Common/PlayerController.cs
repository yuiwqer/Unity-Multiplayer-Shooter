using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    [Header("组件引用")]
    public Transform cameraTransform;   
    public Transform weaponTransform;   
    public Rigidbody bodyRigidbody;     

    [Header("相机设置")]
    public float cameraDistance = 4f;
    public float cameraHeight = 1.5f;
    public float cameraRightOffset = 0.8f;
    public float mouseSensitivity = 3f;
    private float _currentPitch = 15f;
    private Vector3 _moveDir;
    public float moveSpeed = 8f;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsOwner)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (cameraTransform != null)
            {
                // ★ 核心修复：使用 GetComponentInChildren，允许在子节点找 Camera
                var cam = cameraTransform.GetComponentInChildren<Camera>();
                if (cam != null)
                {
                    CameraService.Instance.RegisterMainCamera(cam);
                    Debug.Log($"[PlayerController] 成功注册相机: {cam.name}");
                }
                else
                {
                    Debug.LogError("[PlayerController] 在 cameraTransform 及其子节点上找不到 Camera 组件！");
                }
            }
        }
        else
        {
            // 别人的相机直接销毁
            if (cameraTransform != null)
            {
                var cam = cameraTransform.GetComponentInChildren<Camera>();
                if (cam != null) Destroy(cam.gameObject);
            }
            if (bodyRigidbody != null) 
            {
                bodyRigidbody.useGravity = true; 
                bodyRigidbody.freezeRotation = true;
            }
        }
    }

    void Update()
    {
        if (!IsSpawned || !IsOwner || InputService.Instance == null) return;
        HandleCameraAndRotation();
        CalculateMovement();
    }

    void FixedUpdate()
    {
        if (!IsSpawned || !IsOwner) return;
        ApplyMovement();
    }

    private void HandleCameraAndRotation()
    {
        float mouseX = InputService.Instance.GetMouseX() * mouseSensitivity;
        transform.Rotate(Vector3.up, mouseX * 150f * Time.deltaTime);

        float mouseY = InputService.Instance.GetMouseY() * mouseSensitivity;
        _currentPitch -= mouseY * 100f * Time.deltaTime;
        _currentPitch = Mathf.Clamp(_currentPitch, -30f, 70f);

        Quaternion camRotation = Quaternion.Euler(_currentPitch, transform.eulerAngles.y, 0f);
        
        Vector3 lookAtPos = transform.position + Vector3.up * cameraHeight;
        Vector3 rightDir = camRotation * Vector3.right;
        Vector3 camPos = lookAtPos - (camRotation * Vector3.forward) * cameraDistance + rightDir * cameraRightOffset;

        if (cameraTransform != null)
        {
            cameraTransform.position = camPos;
            cameraTransform.rotation = camRotation;
        }

        if (weaponTransform != null)
        {
            weaponTransform.rotation = cameraTransform.rotation;
        }
    }

    private void CalculateMovement()
    {
        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight = cameraTransform.right;
        camForward.y = 0; camRight.y = 0;
        camForward.Normalize(); camRight.Normalize();

        float h = InputService.Instance.GetHorizontalAxis();
        float v = InputService.Instance.GetVerticalAxis();
        _moveDir = (camForward * v + camRight * h).normalized;
    }

    private void ApplyMovement()
    {
        if (bodyRigidbody == null) return;
        Vector3 targetVelocity = _moveDir * moveSpeed;
        targetVelocity.y = bodyRigidbody.velocity.y;
        bodyRigidbody.velocity = targetVelocity;
    }
}