using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SliderControllerBasic
{
    protected GameObject _slider;
    protected Entity _hostEntity;
    protected Image _fill;
    protected Image smooth;
    protected Image _backGround;
    private float _smoothSpeed;
    private float value;
    private float value_s;
    private int _type;
    private int _positionLayer;
    private bool _hideWhenFull;
    private bool _moveSlider;
    public virtual void SliderInitialize(GameObject sliderObject)
    {
        _slider = sliderObject;
        _fill = _slider.transform.Find("Fill").GetComponent<Image>();
        smooth = _slider.transform.Find("Smooth").GetComponent<Image>();
        _backGround = _slider.transform.Find("Background").GetComponent<Image>();
    }
    public virtual void SetHostEntity(Entity hostEntity, float smoothSpeed, int type, int positionLayer, bool hideWhenFull, bool moveSlider)
    {
        value = 0;
        value_s = 0;
        _type = type;
        _hostEntity = hostEntity;
        _hideWhenFull = hideWhenFull;
        _moveSlider = moveSlider;
        _positionLayer = positionLayer;
        _smoothSpeed = smoothSpeed;
        Move();
        _slider.SetActive(true);
    }
    public void Move()
    {
        float y = 0.25f + _positionLayer * 0.081f;
        _slider.transform.position = _hostEntity.transform.position + y * Vector3.down;
    }
    protected void SetRate(float rate)
    {
        if (value != rate)
        {
            if (rate < 1)
            {
                if (_hideWhenFull && value == 1)
                {
                    _slider.transform.position = _hostEntity.transform.position + 0.25f * Vector3.down;
                    _fill.enabled = true;
                    smooth.enabled = true;
                    _backGround.enabled = true;
                }
                value = rate;
                _fill.fillAmount = value;
            }
            else
            {
                value = 1;
                _fill.fillAmount = 1;
                if (_hideWhenFull)
                {
                    _fill.enabled = false;
                    smooth.enabled = false;
                    _backGround.enabled = false;
                }
            }
        }
    }
    protected virtual void SetRateOperations()
    {

    }
    public void FixedUpdate()
    {
        if (_hostEntity.Stats.IsActive)
        {
            if (_fill.enabled && _moveSlider)
            {
                Move();
            }
            SetRateOperations();
            if (value_s != value)
            {
                if (value_s > value)
                {
                    if (value_s - value >= 0.005f)
                    {
                        value_s -= _smoothSpeed * (value_s - value) * Time.fixedDeltaTime;
                    }
                    else
                    {
                        value_s = value;
                    }
                }
                else
                {
                    value_s = value;
                }
                smooth.fillAmount = value_s;
            }
        }
        else
        {
            ReturnSlider();
        }
    }
    public void ReturnSlider()
    {
        _hostEntity = null;
        SlidersManager.Manager.ReturnSlider(this, _type);
        _slider.gameObject.SetActive(false);
    }
}
