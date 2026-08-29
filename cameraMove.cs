using UnityEngine;

public class FreeCameraController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 8f;
    public float sprintMultiplier = 1.8f;

    [Header("Mouse Look")]
    public float lookSensitivity = 2f;
    public float verticalClamp = 80f;

    private float yaw;
    private float pitch;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = angles.x;
    }

    void Update()
    {
        HandleMovement();
        HandleMouseLook();
    }

    void HandleMovement()
    {
        float speed = moveSpeed;

        if (Input.GetKey(KeyCode.LeftShift))
            speed *= sprintMultiplier;

        Vector3 input =
            new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));

        Vector3 move = transform.TransformDirection(input.normalized) * speed * Time.deltaTime;

        transform.position += move;
    }

    void HandleMouseLook()
    {
        // Hold left mouse to rotate
        if (Input.GetMouseButton(0))
        {
            float mouseX = Input.GetAxis("Mouse X") * lookSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * lookSensitivity;

            yaw += mouseX;
            pitch -= mouseY;

            pitch = Mathf.Clamp(pitch, -verticalClamp, verticalClamp);

            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}