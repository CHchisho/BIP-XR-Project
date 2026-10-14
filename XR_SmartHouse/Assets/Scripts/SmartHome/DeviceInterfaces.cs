namespace SmartHouse
{
    /// <summary>A device that is simply on or off (lamp, plug). Lets one Home Assistant link drive any of them.</summary>
    public interface IToggleDevice
    {
        bool IsOn { get; }
        void SetOn(bool on);
    }

    /// <summary>An on/off device that can also be dimmed.</summary>
    public interface IDimmableDevice : IToggleDevice
    {
        /// <summary>0..1</summary>
        float Brightness { get; }
        void SetBrightness(float brightness01);
    }

    /// <summary>
    /// A device with a value that can be dragged up or down in VR (or scrolled on the desktop):
    /// dimmer brightness, thermostat temperature.
    /// </summary>
    public interface IAdjustable
    {
        /// <summary>Change the value; 1 = the full range upwards, -1 = the full range downwards.</summary>
        void Adjust(float amount);
    }

    /// <summary>A device that reports its power draw (smart plugs).</summary>
    public interface IPowerMeter
    {
        void SetPower(float watts);
    }
}
