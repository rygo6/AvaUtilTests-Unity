using System.Collections;
using UnityEngine;
using GeoTetra.GTEventSystem;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace GeoTetra.GTCamera
{
	public class CameraOrbit : EventReceiverRelay
	{
		[SerializeField]
		CameraView _orbitView;

		[SerializeField]
		float m_MomemtumFadeRate = 8.0f;

		[SerializeField] 
		float _xSpeed = 10.0f;
	
		[SerializeField] 
		float _ySpeed = 10.0f;
		
		[SerializeField] 
		float _yMinLimit = -80f;
	
		[SerializeField] 
		float _yMaxLimit = 80f;
	
		[SerializeField] 
		float _pinchSpeed = 10.0f;
	
		[SerializeField] 
		float _minZoom = -15f;
	
		[SerializeField] 
		float _maxZoom = -5f;
	
		[SerializeField] 
		float _strafeSpeed = 1.0f;
		
		const float MaxMomentumWait = .05f;
		const float k_NoInputThreshold = float.Epsilon;
		Coroutine m_NoInputCoroutine;
		bool m_ThisDragging;
		Vector3 _panDelta;
		Vector2 _panMomentumTimer;
		Vector2 _panSmoothDamp;
		Vector2 _orbitDelta;
		Vector2 _orbitMomentumTimer;
		float _zoomDelta;
		float _zoomMomentumTimer;
		float _zoomSmoothDamp;
		
		public override void OnPointerUpdate(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
		{
			if (!m_ThisDragging)
				return;

			if (module.leftClick.action.IsPressed())
			{
				ProcessOrbit(data);
			}
			else if (module.rightClick.action.IsPressed())
			{
				Pan(data);
			}
			else if (module.middleClick.action.IsPressed())
			{
				Zoom(data);
			}

			MomentumFade();
		}

		public override void OnPointerClick(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
		{
		}

		public override void OnPointerEnter(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
		{
		}

		public override void OnPointerExit(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
		{
		}

		public override void OnPointerDown(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
		{
			data.useDragThreshold = false;
		}

		public override void OnPointerUp(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
		{
		}

		public override void OnBeginDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
		{
			if (m_NoInputCoroutine != null)
				StopCoroutine(m_NoInputCoroutine);

			m_ThisDragging = true;
			_orbitMomentumTimer.x = 0f;
			_orbitMomentumTimer.y = 0f;
			_zoomMomentumTimer = 0f;
			_panMomentumTimer.x = 0f;
			_panMomentumTimer.y = 0f;
		}

		public override void OnDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
		{
		}

		public override void OnEndDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
		{
			m_NoInputCoroutine = StartCoroutine(NoInputCoroutine());
			m_ThisDragging = false;
		}

		void MomentumFade()
		{
			if (_orbitMomentumTimer.x >= 0f)
			{
				_orbitMomentumTimer.x -= Time.deltaTime;
			}
			if (_orbitMomentumTimer.y >= 0f)
			{
				_orbitMomentumTimer.y -= Time.deltaTime;	
			}
			if (_zoomMomentumTimer >= 0f)
			{
				_zoomMomentumTimer -= Time.deltaTime;
			}
			if (_panMomentumTimer.x >= 0f)
			{
				_panMomentumTimer.x -= Time.deltaTime;
			}
			if (_panMomentumTimer.y >= 0f)
			{
				_panMomentumTimer.y -= Time.deltaTime;
			}
		}

		IEnumerator NoInputCoroutine()
		{
			while (_orbitDelta.sqrMagnitude > k_NoInputThreshold ||
			       Mathf.Abs(_zoomDelta) > k_NoInputThreshold || 
			       _panDelta.sqrMagnitude > k_NoInputThreshold)
			{
				NoInput();
				yield return null;
			}

			m_NoInputCoroutine = null;
		}
		
		void NoInput()
		{
			_orbitDelta = Vector2.Lerp(_orbitDelta, Vector2.zero, Time.deltaTime * m_MomemtumFadeRate);
			_zoomDelta = Mathf.Lerp(_zoomDelta, 0f, Time.deltaTime * m_MomemtumFadeRate);
			_panDelta = Vector3.Lerp(_panDelta, Vector3.zero, Time.deltaTime * m_MomemtumFadeRate);						
		
			//zoom
			Vector3 vector3 = _orbitView.transform.localPosition;
			vector3.z += _zoomDelta;
			if (vector3.z < _maxZoom)
			{
				vector3.z = _maxZoom;
			}
			else if (vector3.z > _minZoom)
			{
				vector3.z = _minZoom;
			}
			_orbitView.transform.localPosition = vector3;		
		
			//rotate turntable
			vector3 = transform.eulerAngles;
			vector3.y += _orbitDelta.x;
			transform.eulerAngles = vector3;
			
			vector3 = transform.localEulerAngles;
			vector3.x -= _orbitDelta.y;
			vector3.x = ClampAngleY(vector3.x, _yMinLimit, _yMaxLimit);				
			transform.localEulerAngles = vector3;
			
			//pan
			transform.Translate(_panDelta.x, _panDelta.y, 0, Space.Self);
		}

		void ProcessOrbit(PointerEventData eventData)
		{		
			Vector2 incomingInputDelta = new Vector2
			{
				x = eventData.delta.x * _xSpeed * .02f,
				y = eventData.delta.y * _ySpeed * .02f
			};
			
			float momentumWait = Mathf.Clamp(incomingInputDelta.magnitude / 8f, 0f, MaxMomentumWait);
			
			if ((_orbitDelta.x > 0 && incomingInputDelta.x > _orbitDelta.x) ||
			  (_orbitDelta.x < 0 && incomingInputDelta.x < _orbitDelta.x) ||
			  _orbitDelta.x == 0)
			{
				_orbitDelta.x = incomingInputDelta.x;
				_orbitMomentumTimer.x = momentumWait;
			}
			else if (_orbitMomentumTimer.x <= 0)
			{
				_orbitDelta.x = incomingInputDelta.x;			
			}
		
			if ((_orbitDelta.y > 0 && incomingInputDelta.y > _orbitDelta.y) ||
			  (_orbitDelta.y < 0 && incomingInputDelta.y < _orbitDelta.y) ||
			  _orbitDelta.y == 0)
			{
				_orbitDelta.y = incomingInputDelta.y;
				_orbitMomentumTimer.y = momentumWait;
			}
			else if (_orbitMomentumTimer.y <= 0)
			{
				_orbitDelta.y = incomingInputDelta.y;				
			}
		
			Vector3 newRotation = transform.eulerAngles;
			newRotation.y += _orbitDelta.x;
			transform.eulerAngles = newRotation;
			
			Vector3 newLocalRotation = transform.localEulerAngles;
			newLocalRotation.x -= _orbitDelta.y;
			newLocalRotation.x = ClampAngleY(newLocalRotation.x, _yMinLimit, _yMaxLimit);				
			transform.localEulerAngles = newLocalRotation;
		}

		void Pan(PointerEventData data)
		{
			Vector2 incomingDeltaPos = data.delta;
			incomingDeltaPos = incomingDeltaPos * .001f * _strafeSpeed;
			
			float momentumWait = Mathf.Clamp(Mathf.Abs(incomingDeltaPos.x / 8f), 0f, MaxMomentumWait);	
			
			if ((_panDelta.x > 0 && incomingDeltaPos.x > _panDelta.x) ||
			    (_panDelta.x < 0 && incomingDeltaPos.x < _panDelta.x) ||
			    _panDelta.x == 0)
			{
				// _panDelta.x = Mathf.SmoothDamp(_panDelta.x, -incomingDeltaPos.x, ref _panSmoothDamp.x, .1f);
				_panDelta.x = incomingDeltaPos.x;
				_panMomentumTimer.x = momentumWait;
			}
			else if (_panMomentumTimer.x <= 0)
			{
				// _panDelta.x = Mathf.SmoothDamp(_panDelta.x, -incomingDeltaPos.x, ref _panSmoothDamp.x, .1f);
				_panDelta.x = incomingDeltaPos.x;
			}

			if ((_panDelta.y > 0 && incomingDeltaPos.y > _panDelta.y) ||
			    (_panDelta.y < 0 && incomingDeltaPos.y < _panDelta.y) ||
			    _panDelta.y == 0)
			{
				// _panDelta.y = Mathf.SmoothDamp(_panDelta.y, -incomingDeltaPos.y, ref _panSmoothDamp.y, .1f);
				_panDelta.y = -incomingDeltaPos.y;
				_panMomentumTimer.y = momentumWait;
			}
			else if (_panMomentumTimer.x <= 0)
			{
				// _panDelta.y = Mathf.SmoothDamp(_panDelta.y, -incomingDeltaPos.y, ref _panSmoothDamp.y, .1f);	
				_panDelta.y = -incomingDeltaPos.y;
			}

			transform.Translate(_panDelta.x, _panDelta.y, 0, Space.Self);
		}

		void Zoom(PointerEventData data)
		{
			float incomingZoomDelta = data.delta.y;
			incomingZoomDelta = incomingZoomDelta * _pinchSpeed * 0.02f;
			
			float momentumWait = Mathf.Clamp(incomingZoomDelta, 0f, MaxMomentumWait);
			if ((_zoomDelta > 0 && incomingZoomDelta > _zoomDelta) ||
			  (_zoomDelta < 0 && incomingZoomDelta < _zoomDelta) ||
			  _zoomDelta == 0)
			{
				// _zoomDelta = Mathf.SmoothDamp(_zoomDelta, incomingZoomDelta, ref _zoomSmoothDamp, .1f);
				_zoomDelta = incomingZoomDelta;
				_zoomMomentumTimer = momentumWait;
			}
			else if (_zoomMomentumTimer <= 0)
			{
				// _zoomDelta = Mathf.SmoothDamp(_zoomDelta, incomingZoomDelta, ref _zoomSmoothDamp, .1f);		
				_zoomDelta = incomingZoomDelta;
			}			
		
			Vector3 newPos = _orbitView.transform.localPosition;
			newPos.z += _zoomDelta;
			if (newPos.z < _maxZoom)
			{
				newPos.z = _maxZoom;
			}
			else if (newPos.z > _minZoom)
			{
				newPos.z = _minZoom;
			}
			_orbitView.transform.localPosition = newPos;		
		}

		float ClampAngleY(float angle, float min, float max)
		{
			if (angle < -180)
			{
				angle += 360;
			}
			if (angle > 180)
			{
				angle -= 360;
			}
			return Mathf.Clamp(angle, min, max);
		}
	}
}