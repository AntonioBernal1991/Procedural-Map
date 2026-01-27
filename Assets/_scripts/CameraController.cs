using UnityEngine;


// Moves Camera with keys, optionally following a target.
public class CameraController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 10f; 
    
    [Header("Follow Settings")]
    [SerializeField] private bool useFollow = false;
    [SerializeField] private Transform followTarget;
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 10f, -10f);
    [SerializeField] private float followSmooth = 10f;
    [SerializeField] private bool lookAtTarget = true;

    void Update()
    {
        // If a dedicated auto-forward controller is present, don't also move this transform.
        if (GetComponent<AutoForwardCameraController>() != null)
        {
            return;
        }

        if (useFollow && followTarget != null)
        {
            HandleFollow();
        }
        else
    {
        HandleMovement();
        }
    }

    private void HandleMovement()
    {
        float moveX = Input.GetAxis("Horizontal"); 
        float moveZ = Input.GetAxis("Vertical");  

        
        Vector3 moveDirection = new Vector3(moveX, 0, moveZ);

        transform.Translate(moveDirection * moveSpeed * Time.deltaTime, Space.World);
    }
    
    private void HandleFollow()
    {
        Vector3 desiredPos = followTarget.position + followOffset;
        transform.position = Vector3.Lerp(transform.position, desiredPos, 1f - Mathf.Exp(-followSmooth * Time.deltaTime));

        if (lookAtTarget)
        {
            transform.LookAt(followTarget.position);
        }
    }
}
