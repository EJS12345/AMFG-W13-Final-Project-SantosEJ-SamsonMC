using UnityEngine;

public class PlayerCameraFollow : MonoBehaviour
{
    [Header("Camera Settings")]
    public Vector3 offset = new Vector3(0, 5, -15);
    public float smoothSpeed = 8f;
    public bool lookAtPlayer = true;

    [Header("Follow Options")]
    public bool followX = true;
    public bool followY = true;
    public bool followZ = false;

    private Vector3 playerPosition;
    private bool hasTarget = false;
    private bool initialized = false;

    // Called by the game manager to set player position
    public void SetPlayerPosition(Vector3 position)
    {
        playerPosition = position;
        hasTarget = true;

        // First time - snap camera to correct position immediately
        if (!initialized)
        {
            Vector3 desiredPosition = playerPosition + offset;
            transform.position = desiredPosition;

            // Look at player
            if (lookAtPlayer)
            {
                transform.LookAt(playerPosition);
            }

            initialized = true;
        }
    }

    void LateUpdate()
    {
        if (!hasTarget) return;

        // Calculate desired camera position
        Vector3 desiredPosition = playerPosition + offset;

        // Build target position based on follow options
        Vector3 targetPosition = desiredPosition;

        if (!followX) targetPosition.x = transform.position.x;
        if (!followY) targetPosition.y = transform.position.y;
        if (!followZ) targetPosition.z = transform.position.z;

        // Smoothly move camera
        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            smoothSpeed * Time.deltaTime
        );

        // Make camera look at player
        if (lookAtPlayer)
        {
            Vector3 lookDirection = playerPosition - transform.position;
            if (lookDirection != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, smoothSpeed * Time.deltaTime);
            }
        }
    }
}