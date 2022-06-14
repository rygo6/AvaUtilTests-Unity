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
		float _xSpeed = 10.0f;
	
		[SerializeField] 
		float _ySpeed = 10.0f;

		[SerializeField] 
		float _xMinLimit = 20f;
	
		[SerializeField] 
		float _xMaxLimit = 180f;
	
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
	
		[SerializeField] 
		Vector3 _minStrafe = new Vector3(-2.0f, -2.0f, -2.0f);
	
		[SerializeField] 
		Vector3 _maxStrafe = new Vector3(2.0f, 2.0f, 2.0f);
		
		[SerializeField] 
		Axis _strafeAxis;

		Plane _axisPlane;

		const float MaxMomentumWait = .05f;
		
		[System.Serializable]
		public enum Axis
		{
			X,
			Y,
			Z,
			XY,
			XZ,
			YZ,
			XYZ
		}

		void Awake()
		{
			SetAxisPlane();
		}

		void Update()
		{			
			MomentumFade();
		}

		public override void OnPointerUpdate(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
		{
			// Debug.Log(data.pointerPressRaycast.gameObject );
			// if (data.pointerPressRaycast.gameObject != gameObject)
			// 	return;

			if (module.leftClick.action.IsPressed())
			{
				ProcessOrbit(data);
			}
			else if (module.rightClick.action.IsPressed())
			{
				Pan();
			}
			else if (module.middleClick.action.IsPressed())
			{
				Zoom();
			}
			else
			{
				// NoInput();
			}
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
		}

		public override void OnPointerUp(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
		{
		}

		public override void OnBeginDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
		{
		}

		public override void OnDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
		{
		}

		public override void OnEndDrag(ExtendedPointerEventData data, InputSystemUIInputModule module, InteractionData interactionData)
		{
		}

		void SetAxisPlane()
		{
			switch (_strafeAxis)
			{
			case ( Axis.X ):
				_axisPlane = new Plane(Vector3.forward, Vector3.zero);	
				break;
			case ( Axis.Z ):
				_axisPlane = new Plane(Vector3.right, Vector3.zero);				
				break;
			}			
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

		const float _inputSwitchTimeTreshhold = .1f;

		void NoInput()
		{
			_orbitDelta = Vector2.Lerp(_orbitDelta, Vector2.zero, Time.deltaTime * 8);
			_zoomDelta = Mathf.Lerp(_zoomDelta, 0f, Time.deltaTime * 8);
			_panDelta = Vector3.Lerp(_panDelta, Vector3.zero, Time.deltaTime * 8);						
		
			//zoom
			Vector3 vector3 = _orbitView.transform.localPosition;
			vector3.z += _zoomDelta;
			if (vector3.z > _maxZoom)
			{
				vector3.z = _maxZoom;
			}
			else if (vector3.z < _minZoom)
			{
				vector3.z = _minZoom;
			}
			_orbitView.transform.localPosition = vector3;		
		
			//rotate turntable
			vector3 = transform.eulerAngles;
			vector3.y += _orbitDelta.x;
			if (_xMaxLimit != 0 && _xMinLimit != 0)
			{				
				vector3.y = ClampAngleX(vector3.y, _xMinLimit, _xMaxLimit);	
			}
			transform.eulerAngles = vector3;
			vector3 = transform.localEulerAngles;
			vector3.x -= _orbitDelta.y;
			vector3.x = ClampAngleY(vector3.x, _yMinLimit, _yMaxLimit);				
			transform.localEulerAngles = vector3;
		
			Vector3 newPos = transform.position;
			switch (_strafeAxis)
			{
			case Axis.X:
				PanAxis(ref newPos.x, _panDelta.x, _minStrafe.x, _maxStrafe.x);	
				break;
			case Axis.Y:
				PanAxis(ref newPos.y, _panDelta.y, _minStrafe.y, _maxStrafe.y);	
				break;
			case Axis.Z:
				PanAxis(ref newPos.z, _panDelta.z, _minStrafe.z, _maxStrafe.z);		
				break;
			case Axis.XY:
				PanAxis(ref newPos.x, _panDelta.x, _minStrafe.x, _maxStrafe.x);	
				PanAxis(ref newPos.y, _panDelta.y, _minStrafe.y, _maxStrafe.y);				
				break;
			case Axis.YZ:
				PanAxis(ref newPos.y, _panDelta.y, _minStrafe.y, _maxStrafe.y);				
				PanAxis(ref newPos.z, _panDelta.z, _minStrafe.z, _maxStrafe.z);				
				break;
			}
			transform.position = newPos;	
		}

		void ProcessOrbit(PointerEventData eventData)
		{		
			Vector2 incomingInputDelta = new Vector2
			{
				x = eventData.delta.x * _xSpeed * .02f,
				y = eventData.delta.y * _ySpeed * .02f
			};

			//dont remember specifics of this
			float momentumWait = Mathf.Clamp(incomingInputDelta.magnitude / 8f, 0f, MaxMomentumWait);
		
			//if( Mathf.Abs( incomingInputDelta.x ) > Mathf.Abs( inputDelta.x ) )
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
			if (_xMaxLimit != 0 && _xMinLimit != 0)
			{	
				newRotation.y = ClampAngleX(newRotation.y, _xMinLimit, _xMaxLimit);
			}
			transform.eulerAngles = newRotation;
			Vector3 newLocalRotation = transform.localEulerAngles;
			newLocalRotation.x -= _orbitDelta.y;
			newLocalRotation.x = ClampAngleY(newLocalRotation.x, _yMinLimit, _yMaxLimit);				
			transform.localEulerAngles = newLocalRotation;
		}

		Vector2 _orbitDelta;
		Vector2 _orbitMomentumTimer;

		void Pan()
		{
			// Vector3 incomingDeltaPos = (_emptySpace.PointerEventDataList[0].delta + _emptySpace.PointerEventDataList[0].delta) / 2f;
			Vector3 incomingDeltaPos = Vector3.zero;

			incomingDeltaPos = incomingDeltaPos * .001f * _strafeSpeed;
		
			float momentumWait = Mathf.Clamp(Mathf.Abs(incomingDeltaPos.x / 8f), 0f, MaxMomentumWait);			
		
			//strafing
			float rightPlaneAngle = Vector3.Angle(_orbitView.transform.forward, _axisPlane.normal);			
		
			if ((_panDelta.x > 0 && incomingDeltaPos.x > _panDelta.x) ||
			  (_panDelta.x < 0 && incomingDeltaPos.x < _panDelta.x) ||
			  _panDelta.x == 0)
			{
				if (rightPlaneAngle < 90)
					_panDelta.x = Mathf.SmoothDamp(_panDelta.x, incomingDeltaPos.x, ref _panSmoothDamp.x, .1f);
				else
					_panDelta.x = Mathf.SmoothDamp(_panDelta.x, -incomingDeltaPos.x, ref _panSmoothDamp.x, .1f);				
			
				_panMomentumTimer.x = momentumWait;
			}
			else if (_panMomentumTimer.x <= 0)
			{
				if (rightPlaneAngle < 90)
				{
					_panDelta.x = Mathf.SmoothDamp(_panDelta.x, incomingDeltaPos.x, ref _panSmoothDamp.x, .1f);
				}
				else
				{
					_panDelta.x = Mathf.SmoothDamp(_panDelta.x, -incomingDeltaPos.x, ref _panSmoothDamp.x, .1f);	
				}
			}

			if ((_panDelta.y > 0 && incomingDeltaPos.y > _panDelta.y) ||
			  (_panDelta.y < 0 && incomingDeltaPos.y < _panDelta.y) ||
			  _panDelta.y == 0)
			{
				_panDelta.y = Mathf.SmoothDamp(_panDelta.y, -incomingDeltaPos.y, ref _panSmoothDamp.y, .1f);
				_panMomentumTimer.y = momentumWait;
			}
			else if (_panMomentumTimer.x <= 0)
			{
				_panDelta.y = Mathf.SmoothDamp(_panDelta.y, -incomingDeltaPos.y, ref _panSmoothDamp.y, .1f);	
			}

			Vector3 newPos = transform.position;
		
			switch (_strafeAxis)
			{
			case Axis.X:
				PanAxis(ref newPos.x, _panDelta.x, _minStrafe.x, _maxStrafe.x);	
				break;
			case Axis.Y:
				PanAxis(ref newPos.y, _panDelta.y, _minStrafe.y, _maxStrafe.y);	
				break;
			case Axis.Z:
				PanAxis(ref newPos.z, _panDelta.z, _minStrafe.z, _maxStrafe.z);		
				break;
			case Axis.XY:
				PanAxis(ref newPos.x, _panDelta.x, _minStrafe.x, _maxStrafe.x);	
				PanAxis(ref newPos.y, _panDelta.y, _minStrafe.y, _maxStrafe.y);				
				break;
			case Axis.YZ:
				PanAxis(ref newPos.y, _panDelta.y, _minStrafe.y, _maxStrafe.y);				
				PanAxis(ref newPos.z, _panDelta.z, _minStrafe.z, _maxStrafe.z);				
				break;
			}

			transform.position = newPos;
		}

		Vector3 _panDelta;
		Vector2 _panMomentumTimer;
		Vector2 _panSmoothDamp;

		void PanAxis(ref float newPos, float panDelta, float min, float max)
		{
			newPos -= panDelta;
			if (newPos > max)
			{
				newPos = max;
			}
			else if (newPos < min)
			{
				newPos = min;	
			}
		}

		void Zoom()
		{			
			// Vector2 curDist = _emptySpace.PointerEventDataList[0].position - _emptySpace.PointerEventDataList[1].position ;
			// Vector2 prevDist = (_emptySpace.PointerEventDataList[0].position - _emptySpace.PointerEventDataList[0].delta) -
			// 					(_emptySpace.PointerEventDataList[1].position - _emptySpace.PointerEventDataList[1].delta);			
			// float incomingZoomDelta = (curDist.magnitude - prevDist.magnitude);
			float incomingZoomDelta = 0;
			incomingZoomDelta = incomingZoomDelta * _pinchSpeed * 0.02f;
		
			//zooming
			float momentumWait = Mathf.Clamp(incomingZoomDelta, 0f, MaxMomentumWait);
			if ((_zoomDelta > 0 && incomingZoomDelta > _zoomDelta) ||
			  (_zoomDelta < 0 && incomingZoomDelta < _zoomDelta) ||
			  _zoomDelta == 0)
			{
				_zoomDelta = Mathf.SmoothDamp(_zoomDelta, incomingZoomDelta, ref _zoomSmoothDamp, .1f);
				_zoomMomentumTimer = momentumWait;
			}
			else if (_zoomMomentumTimer <= 0)
			{
				_zoomDelta = Mathf.SmoothDamp(_zoomDelta, incomingZoomDelta, ref _zoomSmoothDamp, .1f);		
			}			
		
			Vector3 newPos = _orbitView.transform.localPosition;
			newPos.z += _zoomDelta;
			if (newPos.z > _maxZoom)
			{
				newPos.z = _maxZoom;
			}
			else if (newPos.z < _minZoom)
			{
				newPos.z = _minZoom;
			}
			_orbitView.transform.localPosition = newPos;		
		}

		float _zoomDelta;
		float _zoomMomentumTimer;
		float _zoomSmoothDamp;
		
		static public float ClampAngleX(float angle, float min, float max)
		{
			float minMaxDelta = Mathf.DeltaAngle(min, max);
			if (minMaxDelta < 0f)
			{
				minMaxDelta += 360f;
			}
			float minDeltaAngle = Mathf.DeltaAngle(angle, min);
			if (minDeltaAngle > 360f - minMaxDelta)
			{
				minDeltaAngle -= 360f;
			}	
			if (minDeltaAngle < -minMaxDelta)
			{
				angle = max;
			}
			else if (minDeltaAngle > 0f)
			{
				float minMaxHalfDiff = Mathf.DeltaAngle(min, max) / 2f;
				if (minMaxHalfDiff < 0f)
				{
					minMaxHalfDiff *= -1;
				}
				if (minDeltaAngle < minMaxHalfDiff)
				{
					angle = min;	
				}
				else if (minDeltaAngle > minMaxHalfDiff)
				{
					angle = max;
				}
			}
			return angle;
		}
    
		static public float ClampAngleY(float angle, float min, float max)
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