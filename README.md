# BURNING AGAIN

Este código ha sido extraído del proyecto en curso de Burning Again, un proyecto personal que iba a realizar en Youtube. La explicación de la primera parte del proyecto, el terreno destructible, puede encontrarse en el siguiente enlace:

[![IMAGE ALT TEXT](https://img.youtube.com/vi/7K-KL-ZU7q8/0.jpg)](https://www.youtube.com/watch?v=7K-KL-ZU7q8 "BURNING AGAIN")

En este código se tratan los siguientes temas:

## Mapas de ruido
Como método para generar el terreno se usa perlin noise para conseguir montañas suaves.

## Computer shaders
Como forma de acelerar el proceso de generación de las mallas del terreno, se usan computer shaders para derivar los cálculos repetitivos del algoritmo Marching cubes a la gráfica.

## Personalización basada en optimización de un algoritmo
Como expongo en el video marching cubes, es un algoritmo que consume bastante, por lo que hago una modificación gracias a la que consigo quitarme de encima muchos cálculos, lo que hace que el algoritmo pase a ser "2D" en vez de "3D". 

## Uso de Burst/Jobs en Unity
Para optimizar el código se hace uso de esta funcionalidad de Unity que, aunque añade complejidad al código, lo hace más eficiente. En él se realizan cambios en como el uso de NativeArrays para poder hacer utilizar sistema y Jobs para paralelizar el trabajo.

## Serializado en memoria y carga de los datos de chunks por regiones
Se hace uso de un algoritmo para guardar de golpe en memoria los datos de los chunks modificados para descargar aquellos que estan mucho tiempo sin ser usados.

[!CAUTION]
Esta parte falla todavía dado que por paralelización ahora se accede al mismo archivo varias veces al mismo tiempo. El sistema operativo no permite esto crasheando.

## Sistema de chunks
Se hace un sistema de streaming de chunks que usa una pool genérica, permitiendo así reutilizar chunks ya cargados en escena para evitar instanciar de forma innecesaria los datos de los chunks.