using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraMove : MonoBehaviour
{
    public float speed;
    public float rotateSpeed;
    public bool limitX = false;
    public bool limitY = false;
    public bool limitZ = false;
    public bool enableRotate = false;
    public float[] xLimit = {0.0f, 0.0f};
    public float[] yLimit = {0.0f, 0.0f};
    public float[] zLimit = {0.0f, 0.0f};
    private Vector3 mousePos;

    // Start is called before the first frame update
    void Start()
    {
        mousePos = Input.mousePosition;
    }

    // Update is called once per frame
    void Update()
    {
        // speed control
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll < 0) speed += 5;
        else if (scroll > 0) speed -= 5;
        if (speed < 0) speed = 0;

        // rotate
        if (Input.GetKey(KeyCode.R)) {
            enableRotate = false;
        } else if (Input.GetKey(KeyCode.F)) {
            enableRotate = true;
            mousePos = Input.mousePosition;
        }
        if (enableRotate)
        {
            Vector3 newMousePos = Input.mousePosition;
            if (newMousePos != mousePos) {
                Vector3 delta = (newMousePos - mousePos) * rotateSpeed * Time.deltaTime;
                transform.localEulerAngles += new Vector3(-delta.y, delta.x, 0);
                mousePos = newMousePos;
            }
        }

        // move
        Vector3 movement = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) movement += transform.forward;
        if (Input.GetKey(KeyCode.S)) movement -= transform.forward;
        if (Input.GetKey(KeyCode.A)) movement -= transform.right;
        if (Input.GetKey(KeyCode.D)) movement += transform.right;
        if (movement == Vector3.zero) return;

        // Debug.Log("movement -> " + movement.ToString());
        transform.position += (movement * speed * Time.deltaTime);

        // Limitation
        if (limitX) transform.position = new Vector3(Mathf.Clamp(transform.position.x, xLimit[0], xLimit[1]), transform.position.y, transform.position.z);
        if (limitY) transform.position = new Vector3(transform.position.x, Mathf.Clamp(transform.position.y, yLimit[0], yLimit[1]), transform.position.z);
        if (limitZ) transform.position = new Vector3(transform.position.x, transform.position.y, Mathf.Clamp(transform.position.z, zLimit[0], zLimit[1]));
    }
}
