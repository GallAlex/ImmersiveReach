using System.Collections;
using UnityEngine;
using UnityStandardAssets.CrossPlatformInput;

namespace Controller
{
    public class FirstPersonController2 : MonoBehaviour
    {
        public float m_Speed = 2;
        public MouseLook2 m_MouseLook;

        private Camera m_Camera;
        private GameObject m_Instrument = null;
        private GameObject m_IntrumentCollider;

        private Vector3 m_Input;
        private Vector3 m_MoveDir = Vector3.zero;
        private Bounds m_InstrumentBounds;


        // Use this for initialization
        private void Start()
        {
            m_Camera = Camera.main;
			m_MouseLook.Init(transform , m_Camera.transform);

            StartCoroutine(WaitForInstrument());
        }

        private IEnumerator WaitForInstrument()
        {
            while (m_Instrument == null)
            {
                m_Instrument = GameObject.Find("CameraInstrument");
                yield return null;
            }

            // Move the initialization code here
            m_Instrument.transform.GetPositionAndRotation(out Vector3 instrumentPosition, out Quaternion instrumentRotation);
            m_Instrument.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            m_InstrumentBounds = ObjectHelper.GetObjectBounds(m_Instrument);
            m_Instrument.transform.SetPositionAndRotation(instrumentPosition, instrumentRotation);
            
            m_IntrumentCollider = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_IntrumentCollider.transform.parent = m_Instrument.transform.parent;
            m_IntrumentCollider.GetComponent<Collider>().enabled = false;
            m_IntrumentCollider.GetComponent<MeshRenderer>().enabled = false;
            m_IntrumentCollider.transform.localPosition = m_InstrumentBounds.center + m_Instrument.transform.localPosition;
            m_IntrumentCollider.transform.rotation = m_Instrument.transform.rotation;
            m_IntrumentCollider.transform.localScale = m_InstrumentBounds.extents*2;
        }

        // Update is called once per frame
        private void Update()
        {
            if (m_IntrumentCollider == null)
            {
                return;
            }

            RotateView();
        }

        private void FixedUpdate()
        {
            if (m_IntrumentCollider == null)
            {
                return;
            }

            float speed = m_Speed;
            GetInput();
            // always move along the camera forward as it is the direction that it being aimed at
            Vector3 desiredMove = m_Camera.transform.forward*m_Input.y + m_Camera.transform.right*m_Input.x + m_Camera.transform.up*m_Input.z;
            
            m_MoveDir = desiredMove*speed;

            Vector3 lastPosition = this.transform.position;

            this.transform.position = this.transform.position + m_MoveDir*Time.fixedDeltaTime;

            if ( Physics.CheckBox(m_IntrumentCollider.transform.position, m_IntrumentCollider.transform.localScale/2, m_IntrumentCollider.transform.rotation) )
            {
                this.transform.position = lastPosition;
            }

            m_MouseLook.UpdateCursorLock();
        }

        private void GetInput()
        {
            // Read input
            float horizontal = CrossPlatformInputManager.GetAxis("Horizontal");
            float vertical = CrossPlatformInputManager.GetAxis("Vertical");
            float upDown = CrossPlatformInputManager.GetAxis("UpDown");

            m_Input = new Vector3(horizontal, vertical, upDown);

            // normalize input if it exceeds 1 in combined length:
            if (m_Input.sqrMagnitude > 1)
            {
                m_Input.Normalize();
            }
        }

        private void RotateView()
        {            
            m_MouseLook.LookRotation(transform, m_Camera.transform, m_IntrumentCollider.transform);
        }
    }
}
