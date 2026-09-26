using UnityEngine;

namespace LiarsBar
{
    /// <summary>酒馆环绕相机：自动缓慢环绕，支持拖拽旋转与滚轮缩放。</summary>
    [RequireComponent(typeof(Camera))]
    public class TavernCameraOrbit : MonoBehaviour
    {
        public Vector3 target = new Vector3(0f, 1.15f, 0f);
        public float distance = 4.4f;
        public float height = 1.9f;
        public float autoRotateSpeed = 4.5f;
        public float dragSpeed = 0.28f;
        public float zoomSpeed = 1.2f;
        public float minDistance = 2.2f;
        public float maxDistance = 9f;

        float _yaw = 0f;
        float _pitch = 16f;
        bool _dragging;

        void Start() => Apply();

        void Update()
        {
            if (!_dragging) _yaw += autoRotateSpeed * Time.deltaTime;

            if (Input.GetMouseButtonDown(0)) _dragging = true;
            if (Input.GetMouseButtonUp(0)) _dragging = false;

            if (_dragging)
            {
                _yaw += Input.GetAxis("Mouse X") * dragSpeed * 100f * Time.deltaTime;
                _pitch -= Input.GetAxis("Mouse Y") * dragSpeed * 100f * Time.deltaTime;
                _pitch = Mathf.Clamp(_pitch, -5f, 70f);
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
                distance = Mathf.Clamp(distance - scroll * zoomSpeed, minDistance, maxDistance);

            Apply();
        }

        void Apply()
        {
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            var offset = rot * new Vector3(0f, 0f, -distance);
            transform.position = target + new Vector3(0f, height, 0f) + offset;
            transform.LookAt(target + Vector3.up * height);
        }
    }
}
