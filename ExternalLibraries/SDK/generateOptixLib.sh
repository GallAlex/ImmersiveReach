cd build

if [ "$1" == "executable" ]; then
    cmake .. -DCREATE_SHARED_LIBRARY=OFF
    echo "Generating executable"
else
    cmake .. -DCREATE_SHARED_LIBRARY=ON
    echo "Generating shared library"
fi

cmake --build .

if [ "$1" != "executable" ]; then
    if [[ "$OSTYPE" == "linux-gnu"* ]]; then
        cd lib
        mv liboptixRaycasting.so ../../../../Assets/Plugins
    elif [[ "$OSTYPE" == "cygwin" || "$OSTYPE" == "msys" ]]; then
        cd bin/Debug
        mv *.dll ../../../../../Assets/Plugins
    fi
fi